using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Skia;
using KyoshinEewViewer.Map.Data;
using SkiaSharp;
using System;
using System.Collections.Generic;

namespace KyoshinEewViewer.Map.Layers;

public sealed class LandLayer : MapLayer
{
	public override bool NeedPersistentUpdate => false;

	/// <summary>
	/// 優先して描画するレイヤー
	/// </summary>
	//public LandLayerType PrimaryRenderLayer { get; set; } = LandLayerType.PrimarySubdivisionArea;
	public Dictionary<LandLayerType, Dictionary<int, SKColor>>? CustomColorMap { get; set; }

	private MapData? map;
	public MapData? Map
	{
		get => map;
		set {
			map = value;
			RefleshRequest();
		}
	}

	#region ResourceCache
	private SKPaint LandFill { get; set; } = new SKPaint
	{
		Style = SKPaintStyle.Fill,
		Color = new SKColor(242, 239, 233),
	};
	//private SKPaint SpecialLandFill { get; set; } = new SKPaint
	//{
	//	Style = SKPaintStyle.Fill,
	//	Color = new SKColor(242, 239, 233),
	//};
	private SKPaint OverSeasLandFill { get; set; } = new SKPaint
	{
		Style = SKPaintStyle.Fill,
		Color = new SKColor(169, 169, 169),
	};

	public override void RefreshResourceCache(Control targetControl)
	{
		SKColor FindColorResource(string name)
			=> ((Color)(targetControl.FindResource(name) ?? throw new Exception($"マップリソース {name} が見つかりませんでした"))).ToSKColor();

		LandFill = new SKPaint
		{
			Style = SKPaintStyle.Fill,
			Color = FindColorResource("LandColor"),
			IsAntialias = false,
		};

		//SpecialLandFill = new SKPaint
		//{
		//	Style = SKPaintStyle.Stroke,
		//	Color = SKColors.Red,
		//	MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3),
		//	StrokeWidth = 5,
		//	IsAntialias = true,
		//};

		OverSeasLandFill = new SKPaint
		{
			Style = SKPaintStyle.Fill,
			Color = FindColorResource("OverseasLandColor"),
			IsAntialias = false,
		};
	}
	#endregion
	public override void Render(SKCanvas canvas, bool isAnimating)
	{
		// コントローラーの初期化ができていなければスキップ
		if (Map == null)
			return;
		canvas.Save();
		try
		{
			// 使用するキャッシュのズーム
			var baseZoom = (int)Math.Ceiling(Zoom);
			// 実際のズームに合わせるためのスケール
			var scale = Math.Pow(2, Zoom - baseZoom);
			canvas.Scale((float)scale);
			// 画面座標への変換
			var leftTop = LeftTopLocation.CastLocation().ToPixel(baseZoom);
			canvas.Translate((float)-leftTop.X, (float)-leftTop.Y);

			// 使用するレイヤー決定
			var useLayerType = LandLayerType.EarthquakeInformationSubdivisionArea;
			if (baseZoom > 10)
				useLayerType = LandLayerType.MunicipalityEarthquakeTsunamiArea;

			if (!Map.TryGetLayer(useLayerType, out var layer))
				return;

			RenderRect(ViewAreaRect);
			// 左右に途切れないように補完して描画させる
			if (ViewAreaRect.Bottom > 180)
			{
				canvas.Translate((float)new KyoshinMonitorLib.Location(0, 180).ToPixel(baseZoom).X, 0);

				var fixedRect = ViewAreaRect;
				fixedRect.Y -= 360;

				RenderRect(fixedRect);
			}
			else if (ViewAreaRect.Top < -180)
			{
				canvas.Translate(-(float)new KyoshinMonitorLib.Location(0, 180).ToPixel(baseZoom).X, 0);

				var fixedRect = ViewAreaRect;
				fixedRect.Y += 360;

				RenderRect(fixedRect);
			}

			void RenderRect(RectD subViewArea)
			{
				// とりあえず海外の描画を行う
				RenderOverseas(canvas, baseZoom, subViewArea);

				foreach (var f in layer.FindPolygon(subViewArea))
				{
					if (CustomColorMap != null &&
						CustomColorMap.TryGetValue(useLayerType, out var map) &&
						map.TryGetValue(f.Code ?? -1, out var color))
					{
						var oc = LandFill.Color;
						LandFill.Color = color;
						f.Draw(canvas, baseZoom, LandFill);
						LandFill.Color = oc;
					}
					else
						f.Draw(canvas, baseZoom, LandFill);

					//if (f.Code == 270000)
					//{
					//	var path = f.GetOrCreatePath(Projection, baseZoom);

					//	canvas.Save();
					//	canvas.ClipPath(path);
					//	canvas.DrawPath(path, SpecialLandFill);
					//	canvas.Restore();
					//}
				}

				if (CustomColorMap is Dictionary<LandLayerType, Dictionary<int, SKColor>> colorMap)
					foreach (var cLayerType in colorMap.Keys)
						if (cLayerType != useLayerType && Map.TryGetLayer(cLayerType, out var clayer))
							foreach (var f in clayer.FindPolygon(subViewArea))
								if (colorMap[cLayerType].TryGetValue(f.Code ?? -1, out var color))
								{
									var oc = LandFill.Color;
									LandFill.Color = color;
									f.Draw(canvas, baseZoom, LandFill);
									LandFill.Color = oc;
								}

				//foreach (var f in layer.FindLine(subViewArea))
				//{
				//	switch (f.Type)
				//	{
				//		case FeatureType.AdminBoundary:
				//			if (!InvalidatePrefStroke && baseZoom > 4.5)
				//				f.Draw(canvas, Projection, baseZoom, PrefStroke);
				//			break;
				//		case FeatureType.Coastline:
				//			if (!InvalidateLandStroke && baseZoom > 4.5)
				//				f.Draw(canvas, Projection, baseZoom, CoastlineStroke);
				//			break;
				//		case FeatureType.AreaBoundary:
				//			if (!InvalidateAreaStroke && baseZoom > 4.5)
				//				f.Draw(canvas, Projection, baseZoom, AreaStroke);
				//			break;
				//	}
				//}
			}
		}
		finally
		{
			canvas.Restore();
		}
	}
	/// <summary>
	/// 海外を描画する
	/// </summary>
	private void RenderOverseas(SKCanvas canvas, int baseZoom, RectD subViewArea)
	{
		if (!(Map?.TryGetLayer(LandLayerType.WorldWithoutJapan, out var layer) ?? false))
			return;

		foreach (var f in layer.FindPolygon(subViewArea))
			f.Draw(canvas, baseZoom, OverSeasLandFill);
	}
}
