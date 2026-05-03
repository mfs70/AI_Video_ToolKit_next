using System;
using System.Windows;
using System.Windows.Controls;

namespace AI_Video_ToolKit.UI.Controls
{
    public partial class TimelineControl : UserControl
    {
        private double _duration;
        private bool _internalChange;

        public event Action<TimeSpan>? OnChanged;

        public TimelineControl()
        {
            InitializeComponent();
        }

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
            if (totalFrames <= 0)
            {
                FrameCursor.Visibility = Visibility.Collapsed;
                return;
            }

            FrameCursor.Visibility = Visibility.Visible;
            FrameText.Text = $"{frame}/{totalFrames}";

            var ratio = Math.Clamp(Slider.Value / Slider.Maximum, 0, 1);
            var width = Math.Max(10, Slider.ActualWidth - 10);
            var x = ratio * width;

            Canvas.SetLeft(FrameCursor, Math.Max(0, x - (FrameCursor.ActualWidth / 2)));
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_internalChange) return;
            OnChanged?.Invoke(TimeSpan.FromSeconds(e.NewValue));
        }
    }
}