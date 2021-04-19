using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using KyoshinEewViewer.Map;
using KyoshinEewViewer.Services;
using ReactiveUI;
using System;
using System.Reactive.Linq;

namespace KyoshinEewViewer.Views
{
	public class MainWindow : Window
	{
		public MainWindow()
		{
			InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
		}

		private bool IsFullScreen { get; set; }

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);

			// フルスク化機能
			KeyDown += (s, e) =>
			{
				if (e.Key != Key.F11)
					return;

				if (IsFullScreen)
				{
					SystemDecorations = SystemDecorations.Full;
					WindowState = WindowState.Normal;
					IsFullScreen = false;
					return;
				}
				// すでに最大化されている場合うまくフルスクにならないので一旦通常状態に戻す
				WindowState = WindowState.Normal;
				Dispatcher.UIThread.InvokeAsync(() =>
				{
					SystemDecorations = SystemDecorations.None;
					WindowState = WindowState.Maximized;
					IsFullScreen = true;
				});
			};

			// マップ表示オプションによるボタンの表示コントロール
			ConfigurationService.Default.Map.WhenAnyValue(x => x.DisableManualMapControl).Subscribe(x => this.FindControl<Button>("homeButton").IsVisible = !x);
			this.FindControl<Button>("homeButton").IsVisible = !ConfigurationService.Default.Map.DisableManualMapControl;

			// マップまわりのハンドラ
			map = this.FindControl<MapControl>("map");
			App.Selector?.WhenAnyValue(x => x.SelectedWindowTheme).Where(x => x != null)
					.Subscribe(x => map.RefleshResourceCache());
			var mapHitbox = this.FindControl<Grid>("mapHitbox");
			mapHitbox.PointerMoved += (s, e2) =>
			{
				//if (mapControl1.IsNavigating)
				//	return;
				var pointer = e2.GetCurrentPoint(this);
				var curPos = pointer.Position / ConfigurationService.Default.WindowScale;
				if (pointer.Properties.IsLeftButtonPressed)
				{
					var diff = new PointD(_prevPos.X - curPos.X, _prevPos.Y - curPos.Y);
					map.CenterLocation = (map.CenterLocation.ToPixel(map.Projection, map.Zoom) + diff).ToLocation(map.Projection, map.Zoom);
				}

				_prevPos = curPos;
				//var rect = map.PaddedRect;

				//var centerPos = map.CenterLocation.ToPixel(map.Projection, map.Zoom);
				//var mouseLoc = new PointD(centerPos.X + ((rect.Width / 2) - curPos.X) + rect.Left, centerPos.Y + ((rect.Height / 2) - curPos.Y) + rect.Top).ToLocation(mapControl1.Projection, mapControl1.Zoom);

				//label1.Text = $"Mouse Lat: {mouseLoc.Latitude:0.000000} / Lng: {mouseLoc.Longitude:0.000000}";
			};
			mapHitbox.PointerPressed += (s, e2) =>
			{
				var pointer = e2.GetCurrentPoint(this);
				if (pointer.Properties.IsLeftButtonPressed)
					_prevPos = pointer.Position / ConfigurationService.Default.WindowScale;
			};
			mapHitbox.PointerWheelChanged += (s, e) =>
			{
				var pointer = e.GetCurrentPoint(this);
				var paddedRect = map.PaddedRect;
				var centerPix = map.CenterLocation.ToPixel(map.Projection, map.Zoom);
				var mousePos = pointer.Position / ConfigurationService.Default.WindowScale;
				var mousePix = new PointD(centerPix.X + ((paddedRect.Width / 2) - mousePos.X) + paddedRect.Left, centerPix.Y + ((paddedRect.Height / 2) - mousePos.Y) + paddedRect.Top);
				var mouseLoc = mousePix.ToLocation(map.Projection, map.Zoom);

				var newZoom = map.Zoom + e.Delta.Y * 0.25;

				var newCenterPix = map.CenterLocation.ToPixel(map.Projection, newZoom);
				var goalMousePix = mouseLoc.ToPixel(map.Projection, newZoom);

				var newMousePix = new PointD(newCenterPix.X + ((paddedRect.Width / 2) - mousePos.X) + paddedRect.Left, newCenterPix.Y + ((paddedRect.Height / 2) - mousePos.Y) + paddedRect.Top);

				map.Zoom = newZoom;
				map.CenterLocation = (map.CenterLocation.ToPixel(map.Projection, newZoom) - (goalMousePix - newMousePix)).ToLocation(map.Projection, newZoom);
			};

			map.Zoom = 6;
			map.CenterLocation = new KyoshinMonitorLib.Location(36.474f, 135.264f);

			this.FindControl<Button>("homeButton").Click += (s, e) => 
				map.Navigate(new RectD(new PointD(24.058240, 123.046875), new PointD(45.706479, 146.293945)));
			this.FindControl<Button>("settingsButton").Click += (s, e) => 
				SubWindowsService.Default.ShowSettingWindow();
		}

		MapControl? map;
		Point _prevPos;
	}
}
