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

        private double _inTime = 0.0;
        private double _outTime = 0.0;

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
            _outTime = duration;
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

        public void SetInOut(TimeSpan inTime, TimeSpan outTime)
        {
            _inTime = inTime.TotalSeconds;
            _outTime = outTime.TotalSeconds;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double w = ActualWidth;
            double h = ActualHeight;

            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));

            if (_duration <= 0) return;

            double px(double t) => (t / _duration) * w;

            // IN зона
            dc.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(80, 0, 255, 0)),
                null,
                new Rect(px(_inTime), 0, px(_outTime) - px(_inTime), h));

            // IN линия
            dc.DrawLine(new Pen(Brushes.Lime, 2),
                new Point(px(_inTime), 0),
                new Point(px(_inTime), h));

            // OUT линия
            dc.DrawLine(new Pen(Brushes.Red, 2),
                new Point(px(_outTime), 0),
                new Point(px(_outTime), h));

            // курсор
            dc.DrawLine(new Pen(Brushes.White, 2),
                new Point(px(_currentTime), 0),
                new Point(px(_currentTime), h));
        }

        private void UserControl_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            CaptureMouse();
            OnUserInteraction?.Invoke(true);

            Update(e.GetPosition(this).X);
        }

        private void UserControl_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
                Update(e.GetPosition(this).X);
        }

        private void UserControl_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            ReleaseMouseCapture();
            OnUserInteraction?.Invoke(false);
        }

        private void Update(double x)
        {
            double w = ActualWidth;
            if (w <= 0) return;

            _currentTime = Math.Max(0, Math.Min(_duration, (x / w) * _duration));

            InvalidateVisual();

            if (!_internalUpdate)
                OnTimeChanged?.Invoke(TimeSpan.FromSeconds(_currentTime));
        }
    }
}