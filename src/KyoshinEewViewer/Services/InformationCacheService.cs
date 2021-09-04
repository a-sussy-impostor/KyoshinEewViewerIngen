﻿using KyoshinEewViewer.Core.Models.Events;
using LiteDB;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace KyoshinEewViewer.Services
{
	public class InformationCacheService
	{
		private static InformationCacheService? _default;
		public static InformationCacheService Default => _default ??= new InformationCacheService();

		private LiteDatabase CacheDatabase { get; set; }

		private ILiteCollection<TelegramCacheModel> TelegramCacheTable { get; set; }
		private ILiteCollection<ImageCacheModel> ImageCacheTable { get; set; }

		private ILogger Logger { get; }

#pragma warning disable CS8618 // null 非許容のフィールドには、コンストラクターの終了時に null 以外の値が入っていなければなりません。Null 許容として宣言することをご検討ください。
		public InformationCacheService()
#pragma warning restore CS8618 // null 非許容のフィールドには、コンストラクターの終了時に null 以外の値が入っていなければなりません。Null 許容として宣言することをご検討ください。
		{
			Logger = LoggingService.CreateLogger(this);

			ReloadCache();
			MessageBus.Current.Listen<ApplicationClosing>().Subscribe(x => CacheDatabase?.Dispose());
		}

		public async void ReloadCache()
		{
			try
			{
				CacheDatabase?.Dispose();

				// 最大1秒 ファイルにアクセスできるようになるまで待つ
				if (File.Exists("cache.db"))
				{
					var count = 0;
					while (!CheckFileAccess("cache.db"))
					{
						if (++count > 10) return;
						await Task.Delay(100);
					}

					Logger.LogDebug("check access: " + count);
				}

				try
				{
					CacheDatabase = new LiteDatabase("cache.db");
				}
				catch (LiteException ex)
				{
					Logger.LogWarning("Cache DBの読み込みがLiteDB層で失敗しました " + ex);
					File.Delete("cache.db");
					CacheDatabase = new LiteDatabase("cache.db");
				}
			}
			catch (Exception ex)
			{
				Logger.LogWarning("Cache DBの読み込みに失敗しました テンポラリで行います " + ex);
				CacheDatabase = new LiteDatabase("Filename=:temp:");
			}
			TelegramCacheTable = CacheDatabase.GetCollection<TelegramCacheModel>();
			TelegramCacheTable.EnsureIndex(x => x.Key, true);
			ImageCacheTable = CacheDatabase.GetCollection<ImageCacheModel>();
			ImageCacheTable.EnsureIndex(x => x.Url, true);
		}
		public static bool CheckFileAccess(string filename)
		{
			try
			{
				using var stream = File.Open(filename, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
				return stream.Length > 0;
			}
			catch (IOException)
			{
				return false;
			}
		}

		/// <summary>
		/// Keyを元にキャッシュされたstreamを取得する
		/// </summary>
		public bool TryGetTelegram(string key, out Stream stream)
		{
			var cache = TelegramCacheTable.FindOne(i => i.Key == key);
			if (cache == null)
			{
#pragma warning disable CS8625 // falseなので普通にnullを代入する
				stream = null;
				return false;
#pragma warning restore CS8625
			}
			var memStream = new MemoryStream(cache.Body);
			stream = new GZipStream(memStream, CompressionMode.Decompress);
			return true;
		}

		public async Task<Stream> TryGetOrFetchTelegramAsync(string key, string title, DateTime arrivalTime, Func<Task<Stream>> fetcher)
		{
			if (TryGetTelegram(key, out var stream))
				return stream;

			stream = new MemoryStream();
			using (var body = await fetcher())
				await body.CopyToAsync(stream);

			stream.Seek(0, SeekOrigin.Begin);
			TelegramCacheTable.Insert(new TelegramCacheModel(
				key,
				title,
				arrivalTime,
				CompressStream(stream)));
			CacheDatabase.Commit();

			stream.Seek(0, SeekOrigin.Begin);
			return stream;
		}

		/// <summary>
		/// URLを元にキャッシュされたstreamを取得する
		/// </summary>
		public bool TryGetImage(string url, out SKBitmap bitmap)
		{
			var cache = ImageCacheTable.FindOne(i => i.Url == url);
			if (cache == null)
			{
#pragma warning disable CS8625 // falseなので普通にnullを代入する
				bitmap = null;
				return false;
#pragma warning restore CS8625
			}
			using var memStream = new MemoryStream(cache.Body);
			using var stream = new GZipStream(memStream, CompressionMode.Decompress);
			bitmap = SKBitmap.Decode(stream);
			return true;
		}
		public async Task<SKBitmap> TryGetOrFetchImageAsync(string url, Func<Task<(SKBitmap, DateTime)>> fetcher)
		{
			if (TryGetImage(url, out var bitmap))
				return bitmap;

			var res = await fetcher();
			bitmap = res.Item1;

			using var stream = new MemoryStream();
			bitmap.Encode(stream, SKEncodedImageFormat.Png, 100);

			stream.Seek(0, SeekOrigin.Begin);
			ImageCacheTable.Insert(new ImageCacheModel(
				url,
				res.Item2,
				CompressStream(stream)));
			CacheDatabase.Commit();
			return bitmap;
		}

		/// <summary>
		/// URLを元にキャッシュされたstreamを取得する
		/// </summary>
		public bool TryGetImageAsStream(string url, out Stream stream)
		{
			var cache = ImageCacheTable.FindOne(i => i.Url == url);
			if (cache == null)
			{
#pragma warning disable CS8625 // falseなので普通にnullを代入する
				stream = null;
				return false;
#pragma warning restore CS8625
			}
			var memStream = new MemoryStream(cache.Body);
			stream = new GZipStream(memStream, CompressionMode.Decompress);
			return true;
		}

		public async Task<Stream> TryGetOrFetchImageAsStreamAsync(string url, Func<Task<(Stream, DateTime)>> fetcher)
		{
			if (TryGetImageAsStream(url, out var stream))
				return stream;

			stream = new MemoryStream();
			var resp = await fetcher();
			using (resp.Item1)
				await resp.Item1.CopyToAsync(stream);

			stream.Seek(0, SeekOrigin.Begin);
			ImageCacheTable.Insert(new ImageCacheModel(
				url,
				resp.Item2,
				CompressStream(stream)));
			CacheDatabase.Commit();

			stream.Seek(0, SeekOrigin.Begin);
			return stream;
		}

		private static byte[] CompressStream(Stream body)
		{
			using var outStream = new MemoryStream();
			using (var compressStream = new GZipStream(outStream, CompressionLevel.Optimal))
				body.CopyTo(compressStream);
			return outStream.ToArray();
		}

		public void CleanupCaches()
		{
			CleanupTelegramCache();
			CleanupImageCache();
		}
		private void CleanupTelegramCache()
		{
			Logger.LogDebug("telegram cache cleaning...");
			var s = DateTime.Now;
			CacheDatabase.BeginTrans();
			// 2週間以上経過したものを削除
			TelegramCacheTable.DeleteMany(c => c.ArrivalTime < DateTime.Now.AddDays(-14));
			Logger.LogDebug($"telegram cache cleaning completed: {(DateTime.Now - s).TotalMilliseconds}ms");
		}
		private void CleanupImageCache()
		{
			Logger.LogDebug("image cache cleaning...");
			var s = DateTime.Now;
			CacheDatabase.BeginTrans();
			// 期限が切れたものを削除
			ImageCacheTable.DeleteMany(c => c.ExpireTime < DateTime.Now);
			Logger.LogDebug($"image cache cleaning completed: {(DateTime.Now - s).TotalMilliseconds}ms");
		}
	}

	public record TelegramCacheModel(string Key, string Title, DateTime ArrivalTime, byte[] Body);
	public record ImageCacheModel(string Url, DateTime ExpireTime, byte[] Body);
}
