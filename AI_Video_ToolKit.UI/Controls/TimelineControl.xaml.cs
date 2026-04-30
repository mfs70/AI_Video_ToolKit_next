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

            Slider.Value = time.TotalSeconds;

            TimeText.Text =
                $"{time:mm\\:ss} / {TimeSpan.FromSeconds(_duration):mm\\:ss}";

            _internalChange = false;
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_internalChange) return;

            OnChanged?.Invoke(TimeSpan.FromSeconds(e.NewValue));
        }
    }
}