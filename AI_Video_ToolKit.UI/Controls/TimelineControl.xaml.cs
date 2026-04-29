using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AI_Video_ToolKit.UI.Controls
{
    public partial class TimelineControl : UserControl
    {
        private double _duration = 1.0;
        private double _currentTime = 0.0;

        private bool _isDragging = false;
        private bool _internalUpdate = false;

        public event Action<TimeSpan>? OnTimeChanged;
        public event Action<bool>? OnUserInteraction;

        public TimelineControl()
        {
            InitializeComponent();
        }

        public void SetDuration(double duration)
        {
            _duration = duration;
            InvalidateVisual();
        }

        public void SetCurrentTime(TimeSpan time)
        {
            if (_isDragging) return;

            _internalUpdate = true;
            _currentTime = time.TotalSeconds;
            InvalidateVisual();
            _internalUpdate = false;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double width = ActualWidth;
            double height = ActualHeight;

            dc.DrawRectangle(Brushes.DarkGray, null, new Rect(0, 0, width, height));

            double x = (_currentTime / _duration) * width;

            dc.DrawLine(new Pen(Brushes.Red, 2), new Point(x, 0), new Point(x, height));
        }

        private void UserControl_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;

            e.Handled = true; // 🔥 КЛЮЧ

            Focus(); // 🔥 важно для WPF

            _isDragging = true;
            CaptureMouse();

            OnUserInteraction?.Invoke(true);

            UpdatePosition(e.GetPosition(this).X);
        }

        private void UserControl_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                UpdatePosition(e.GetPosition(this).X);
            }
        }

        private void UserControl_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;

            _isDragging = false;
            ReleaseMouseCapture();

            OnUserInteraction?.Invoke(false);

            e.Handled = true;
        }

        private void UpdatePosition(double x)
        {
            double width = ActualWidth;

            if (width <= 0 || _duration <= 0) return;

            _currentTime = Math.Max(0, Math.Min(_duration, (x / width) * _duration));

            InvalidateVisual();

            if (!_internalUpdate)
            {
                OnTimeChanged?.Invoke(TimeSpan.FromSeconds(_currentTime));
            }
        }
    }
}