﻿using KyoshinEewViewer.CustomControl;
using KyoshinEewViewer.Map;
using KyoshinEewViewer.Map.Projections;
using KyoshinMonitorLib;
using SkiaSharp;
using System;

namespace KyoshinEewViewer.Series.Earthquake.RenderObjects
{
	public class IntensityStationRenderObject : IRenderObject
	{
		public IntensityStationRenderObject(LandLayerType? layerType, string name, Location location, JmaIntensity intensity, bool isRegion)
		{
			Name = name;
			Location = location;
			Intensity = intensity;
			IsRegion = isRegion;
			LayerType = layerType;
		}

		public string Name { get; set; }
		public Location Location { get; set; }
		public JmaIntensity Intensity { get; set; }
		public bool IsRegion { get; }
		public LandLayerType? LayerType { get; }

		private SKPaint? textPaint;

		public void Render(SKCanvas canvas, RectD viewRect, double zoom, PointD leftTopPixel, bool isAnimating, bool isDarkTheme, MapProjection projection)
		{
			if (LayerType == LandLayerType.EarthquakeInformationSubdivisionArea && zoom > 8)
				return;
			if (LayerType == LandLayerType.MunicipalityEarthquakeTsunamiArea && zoom <= 8)
				return;

			var circleSize = zoom * 1.25;
			var circleVector = new PointD(circleSize, circleSize);
			var pointCenter = Location.ToPixel(projection, zoom);
			if (!viewRect.IntersectsWith(new RectD(pointCenter - circleVector, pointCenter + circleVector)))
				return;

			if (!isAnimating && ((LayerType == null && zoom >= 8) ||
				(LayerType == LandLayerType.MunicipalityEarthquakeTsunamiArea && zoom >= 10) ||
				(LayerType == LandLayerType.EarthquakeInformationSubdivisionArea && zoom >= 7.5)))
			{
				// 観測点情報文字の描画
				if (textPaint == null)
					textPaint = new SKPaint
					{
						Style = SKPaintStyle.Fill,
						IsAntialias = true,
						Typeface = FixedObjectRenderer.MainTypeface,
						TextSize = 14,
						Color = isDarkTheme ? SKColors.White : SKColors.Black
					};
				textPaint.TextSize = (float)Math.Max(circleSize * 1.5, 14);
				canvas.DrawText(Name, (pointCenter - leftTopPixel + new PointD(circleSize * 1.2, circleSize * .5)).AsSKPoint(), textPaint);
			}

			// 震度アイコンの描画
			FixedObjectRenderer.DrawIntensity(canvas, Intensity, pointCenter - leftTopPixel, (float)(circleSize * 2), true, !IsRegion, false);
		}
	}
}