using DmdataSharp.ApiResponses.V2.Parameters;
using KyoshinEewViewer.Core;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.JmaXmlParser;
using KyoshinEewViewer.Series.Earthquake.Models;
using KyoshinEewViewer.Services;
using KyoshinEewViewer.Services.TelegramPublishers.Dmdata;
using KyoshinMonitorLib;
using ReactiveUI;
using Sentry;
using Splat;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace KyoshinEewViewer.Series.Earthquake.Services;

/// <summary>
/// 地震情報の更新を担う
/// </summary>
public class EarthquakeWatchService : ReactiveObject
{
	private readonly string[] _targetTitles = ["震度速報", "震源に関する情報", "震源・震度に関する情報", "顕著な地震の震源要素更新のお知らせ", "長周期地震動に関する観測情報"];

	private NotificationService? NotificationService { get; }
	public EarthquakeStationParameterResponse? Stations { get; private set; }
	public ObservableCollection<EarthquakeEvent> Earthquakes { get; } = [];
	public event Action<EarthquakeEvent, bool>? EarthquakeUpdated;

	public event Action? Failed;
	public event Action? SourceSwitching;
	public event Action<string>? SourceSwitched;

	private SoundCategory SoundCategory { get; } = new("Earthquake", "地震情報");
	private Sound UpdatedSound { get; }
	private Sound IntensityUpdatedSound { get; }
	private Sound UpdatedTrainingSound { get; }

	private ILogger Logger { get; }
	private KyoshinEewViewerConfiguration Config { get; }

	public EarthquakeWatchService(ILogManager logManager, KyoshinEewViewerConfiguration config, NotificationService notificationService, SoundPlayerService soundPlayer, TelegramProvideService telegramProvider, DmdataTelegramPublisher dmdata)
	{
		SplatRegistrations.RegisterLazySingleton<EarthquakeWatchService>();

		Logger = logManager.GetLogger<EarthquakeWatchService>();
		Config = config;
		NotificationService = notificationService;

		UpdatedSound = soundPlayer.RegisterSound(SoundCategory, "Updated", "地震情報の更新", "{int}: 最大震度 [？,0,1,...,6-,6+,7]", new() { { "int", "4" }, });
		IntensityUpdatedSound = soundPlayer.RegisterSound(SoundCategory, "IntensityUpdated", "震度の更新", "{int}: 最大震度 [？,0,1,...,6-,6+,7]", new() { { "int", "4" }, });
		UpdatedTrainingSound = soundPlayer.RegisterSound(SoundCategory, "TrainingUpdated", "地震情報の更新(訓練)", "{int}: 最大震度 [？,0,1,...,6-,6+,7]", new() { { "int", "6+" }, });

		telegramProvider.Subscribe(
			InformationCategory.Earthquake,
			async (s, t) =>
			{
				SourceSwitching?.Invoke();

				if (s.Contains("DM-D.S.S") && Stations == null)
					try
					{
						Stations = await dmdata.GetEarthquakeStationsAsync();
					}
					catch (Exception ex)
					{
						Logger.LogError(ex, "観測点情報取得中に問題が発生しました");
					}

				Earthquakes.Clear();
				foreach (var h in t.OrderBy(h => h.ArrivalTime))
				{
					try
					{
						ProcessInformation(h.Key, await h.GetBodyAsync(), hideNotice: true);
					}
					catch (Exception ex)
					{
						Logger.LogError(ex, "キャッシュ破損疑いのため削除します");
						try
						{
							// キャッシュ破損時用
							h.Cleanup();
							ProcessInformation(h.Key, await h.GetBodyAsync(), hideNotice: true);
						}
						catch (Exception ex2)
						{
							// その他のエラー発生時は処理を中断させる
							Logger.LogError(ex2, "初回電文取得中に問題が発生しました");
						}
						return;
					}
				}
				// 電文データがない(震源情報しかないなどの)データを削除する
				foreach (var eq in Earthquakes.Where(e => e.Fragments.All(f => f is not IntensityInformationFragment and not HypocenterAndIntensityInformationFragment)).ToArray())
					Earthquakes.Remove(eq);

				foreach (var eq in Earthquakes)
					EarthquakeUpdated?.Invoke(eq, true);
				SourceSwitched?.Invoke(s);
			},
			async t =>
			{
				var trans = SentrySdk.StartTransaction("earthquake", "arrived");
				try
				{
					var stream = await t.GetBodyAsync();
					ProcessInformation(t.Key, stream);
					trans.Finish();
				}
				catch (Exception ex)
				{
					trans.Finish(ex);
				}
			},
			s =>
			{
				if (s.isAllFailed)
					Failed?.Invoke();
				else
					SourceSwitching?.Invoke();
			});

		telegramProvider.Subscribe(
			InformationCategory.Tsunami,
			(_, _) =>
			{
				// あくまで震源情報の代わりなので津波情報はとりあえずなにもしない
				// 問題が発生したらなんとかする
				return Task.CompletedTask;
			},
			async t =>
			{
				try
				{
					ProcessTsunamiInformation(t.Key, await t.GetBodyAsync());
				}
				catch (Exception ex)
				{
					Logger.LogError(ex, "津波情報による震源情報の更新に失敗しました。");
				}
			},
			_ => { }
		);
	}

	public void ProcessTsunamiInformation(string id, Stream stream, bool hideNotice = false)
	{
		using (stream)
		{
			using var report = new JmaXmlDocument(stream);
			if (report.Control.Title != "津波警報・注意報・予報a")
				return;

			var fragments = EarthquakeInformationFragment.CreateFromTsunamiJmxXmlDocument(id, report);
			foreach (var (EventId, Fragment) in fragments)
			{
				var eq = Earthquakes.FirstOrDefault(e => e.EventId == EventId);
				if (eq == null)
				{
					Logger.LogWarning($"イベントID {EventId} が見つからなかったため津波情報による震源情報の更新を行いませんでした。");
					continue;
				}
				eq.ProcessTelegram(id, report);
				if (!hideNotice)
					EarthquakeUpdated?.Invoke(eq, false);
			}
		}
	}
	// MEMO: 内部で stream は dispose します
	public EarthquakeEvent? ProcessInformation(string id, Stream stream, bool dryRun = false, bool hideNotice = false)
	{
		using (stream)
		{
			using var report = new JmaXmlDocument(stream);

			try
			{
				// サポート外であれば見なかったことにする
				if (!_targetTitles.Contains(report.Control.Title))
					return null;

				// 保存されている Earthquake インスタンスを抜き出してくる
				var eq = Earthquakes.FirstOrDefault(e => e.EventId == report.Head.EventId);
				if (eq == null || dryRun)
				{
					eq = new EarthquakeEvent(report.Head.EventId);
					if (!dryRun)
						Earthquakes.Insert(0, eq);
				}

				// 情報更新前の震度
				var prevInt = eq.Intensity;

				// 情報を処理
				var fragment = eq.ProcessTelegram(id, report);
				if (!hideNotice)
				{
					EarthquakeUpdated?.Invoke(eq, false);
					if (!dryRun)
					{
						var intStr = eq.Intensity.ToShortString().Replace('*', '-');
						if (
							(!eq.IsTraining || !UpdatedTrainingSound.Play(new() { { "int", intStr } })) &&
							(eq.Intensity == prevInt || !IntensityUpdatedSound.Play(new() { { "int", intStr } }))
						)
							UpdatedSound.Play(new() { { "int", intStr } });
						if (Config.Notification.GotEq && fragment != null)
							NotificationService?.Notify($"{fragment.Title}", eq.GetNotificationMessage());
					}
				}
				return eq;
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "デシリアライズ時に例外が発生しました");
				return null;
			}
		}
	}
}
