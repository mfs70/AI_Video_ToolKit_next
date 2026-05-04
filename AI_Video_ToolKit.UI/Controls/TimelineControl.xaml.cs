using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AI_Video_ToolKit.UI.Controls
{
    public partial class TimelineControl : UserControl
    {
        private double _duration;
        private bool _internalChange;

        public event Action<TimeSpan>? OnChanged;

        public TimelineControl() => InitializeComponent();

        public void SetDuration(double duration)
        {
            _duration = duration;
            Slider.Maximum = duration;
        }

        public void SetCurrentTime(TimeSpan time)
        {
            _internalChange = true;
            Slider.Value = Math.Clamp(time.TotalSeconds, 0, Slider.Maximum);
            TimeText.Text = $"{time:mm\\:ss} / {TimeSpan.FromSeconds(_duration):mm\\:ss}";
            _internalChange = false;
        }

        public void SetFrameInfo(long frame, long totalFrames)
        {
            if (totalFrames <= 0) { FrameCursor.Visibility = Visibility.Collapsed; return; }
            FrameCursor.Visibility = Visibility.Visible;
            FrameText.Text = $"{frame}/{totalFrames}";
            var ratio = Slider.Maximum > 0 ? Math.Clamp(Slider.Value / Slider.Maximum, 0, 1) : 0;
            var width = Math.Max(10, Slider.ActualWidth - 10);
            Canvas.SetLeft(FrameCursor, Math.Max(0, ratio * width - (FrameCursor.ActualWidth / 2)));
        }

        public void SetMarkers(TimeSpan? input, TimeSpan? output, IReadOnlyCollection<TimeSpan> cuts)
        {
            var markerCanvas = FindName("MarkerCanvas") as Canvas;
            if (markerCanvas == null) return;
            markerCanvas.Children.Clear();
            if (_duration <= 0) return;

            DrawMarker(input, Brushes.LimeGreen, 2);
            DrawMarker(output, Brushes.IndianRed, 2);
            foreach (var c in cuts) DrawMarker(c, Brushes.White, 1);

            markerCanvas.Children.Add(FrameCursor);
        }

        private void DrawMarker(TimeSpan? time, Brush color, double width)
        {
            if (time == null) return;
            var markerCanvas = FindName("MarkerCanvas") as Canvas;
            if (markerCanvas == null) return;

            var sliderWidth = Slider.ActualWidth > 1 ? Slider.ActualWidth : Slider.Width;
            if (sliderWidth <= 1) sliderWidth = ActualWidth > 1 ? ActualWidth - 20 : 300;

            var ratio = Math.Clamp(time.Value.TotalSeconds / _duration, 0, 1);
            var x = ratio * Math.Max(10, sliderWidth - 10);

            var rect = new Rectangle { Width = width, Height = 16, Fill = color };
            Canvas.SetLeft(rect, Math.Max(0, x));
            Canvas.SetTop(rect, 2);
            markerCanvas.Children.Add(rect);
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_internalChange) return;
            OnChanged?.Invoke(TimeSpan.FromSeconds(e.NewValue));
        }
    }
}
