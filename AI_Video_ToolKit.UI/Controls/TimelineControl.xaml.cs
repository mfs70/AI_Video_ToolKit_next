using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AI_Video_ToolKit.UI.Controls
{
    public partial class TimelineControl : UserControl
    {
        public event Action<TimeSpan>? OnTimeChanged;

        private double _duration = 100;

        private Line _playhead = new() { Stroke = Brushes.White, StrokeThickness = 2 };

        private bool _dragging;

        public TimelineControl()
        {
            InitializeComponent();

            TimelineCanvas.Children.Add(_playhead);
        }

        public void SetDuration(double seconds)
        {
            _duration = seconds;
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragging = true;
            Update(e);
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging)
                Update(e);
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;
        }

        private void Update(MouseEventArgs e)
        {
            var pos = e.GetPosition(TimelineCanvas);

            double time = (pos.X / TimelineCanvas.ActualWidth) * _duration;

            OnTimeChanged?.Invoke(TimeSpan.FromSeconds(time));

            _playhead.X1 = pos.X;
            _playhead.X2 = pos.X;
            _playhead.Y1 = 0;
            _playhead.Y2 = TimelineCanvas.Height;
        }
    }
}