// Файл: AI_Video_ToolKit.UI/Controls/TimelineControl.xaml.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AI_Video_ToolKit.UI.Controls
{
    public partial class TimelineControl : UserControl
    {
        public enum MarkerSelection
        {
            None,
            Input,
            Output,
            Cut
        }

        private double _duration;

        private bool _internalChange;
        private bool _isDraggingSlider;

        private TimeSpan? _inputMarker;
        private TimeSpan? _outputMarker;

        private IReadOnlyCollection<TimeSpan> _cutMarkers =
            Array.Empty<TimeSpan>();

        private TimeSpan? _selectedMarkerTime;

        private MarkerSelection _selectedType =
            MarkerSelection.None;

        public event Action<TimeSpan>? OnChanged;

        public TimelineControl()
        {
            InitializeComponent();

            Loaded += TimelineControl_Loaded;
        }

        public MarkerSelection SelectedMarkerType =>
            _selectedType;

        public TimeSpan? SelectedMarkerTime =>
            _selectedMarkerTime;

        private void TimelineControl_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (FindName("MarkerCanvas") is Canvas canvas)
            {
                canvas.Focusable = false;

                canvas.MouseLeftButtonDown -=
                    MarkerCanvas_MouseLeftButtonDown;

                canvas.MouseLeftButtonDown +=
                    MarkerCanvas_MouseLeftButtonDown;
            }

            Slider.PreviewMouseLeftButtonDown -=
                Slider_PreviewMouseLeftButtonDown;

            Slider.PreviewMouseLeftButtonUp -=
                Slider_PreviewMouseLeftButtonUp;

            Slider.PreviewMouseLeftButtonDown +=
                Slider_PreviewMouseLeftButtonDown;

            Slider.PreviewMouseLeftButtonUp +=
                Slider_PreviewMouseLeftButtonUp;
        }

        private void Slider_PreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            _isDraggingSlider = true;

            Keyboard.ClearFocus();
        }

        private void Slider_PreviewMouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            _isDraggingSlider = false;

            OnChanged?.Invoke(
                TimeSpan.FromSeconds(Slider.Value));
        }

        public void SetDuration(double duration)
        {
            _duration = Math.Max(0, duration);

            Slider.Maximum = _duration;
        }

        public void SetCurrentTime(TimeSpan time)
        {
            _internalChange = true;

            Slider.Value = Math.Clamp(
                time.TotalSeconds,
                0,
                Slider.Maximum);

            TimeText.Text =
                $"{time:mm\\:ss} / {TimeSpan.FromSeconds(_duration):mm\\:ss}";

            UpdateFrameCursorPosition();

            _internalChange = false;
        }

        public void SetFrameInfo(
            long frame,
            long totalFrames)
        {
            if (totalFrames <= 0)
            {
                FrameCursor.Visibility =
                    Visibility.Collapsed;

                return;
            }

            FrameCursor.Visibility =
                Visibility.Visible;

            FrameText.Text =
                $"{frame}/{totalFrames}";

            UpdateFrameCursorPosition();
        }

        private void UpdateFrameCursorPosition()
        {
            if (_duration <= 0)
                return;

            var ratio =
                Slider.Maximum > 0
                    ? Math.Clamp(
                        Slider.Value / Slider.Maximum,
                        0,
                        1)
                    : 0;

            var width =
                Math.Max(10, Slider.ActualWidth - 10);

            Canvas.SetLeft(
                FrameCursor,
                Math.Max(
                    0,
                    ratio * width -
                    (FrameCursor.ActualWidth / 2)));
        }

        public void SetMarkers(
            TimeSpan? input,
            TimeSpan? output,
            IReadOnlyCollection<TimeSpan> cuts)
        {
            _inputMarker = input;
            _outputMarker = output;
            _cutMarkers = cuts;

            var markerCanvas =
                FindName("MarkerCanvas") as Canvas;

            if (markerCanvas == null)
                return;

            RemoveOldMarkerVisuals(markerCanvas);

            if (_duration <= 0)
                return;

            DrawMarker(
                markerCanvas,
                input,
                Brushes.LimeGreen,
                3,
                MarkerSelection.Input);

            DrawMarker(
                markerCanvas,
                output,
                Brushes.IndianRed,
                3,
                MarkerSelection.Output);

            foreach (var cut in cuts.OrderBy(x => x))
            {
                DrawMarker(
                    markerCanvas,
                    cut,
                    Brushes.White,
                    2,
                    MarkerSelection.Cut);
            }
        }

        private void RemoveOldMarkerVisuals(
            Canvas markerCanvas)
        {
            var toRemove =
                markerCanvas.Children
                    .OfType<FrameworkElement>()
                    .Where(x =>
                        x.Tag is string tag &&
                        tag == "MARKER")
                    .ToList();

            foreach (var item in toRemove)
            {
                markerCanvas.Children.Remove(item);
            }
        }

        private void DrawMarker(
            Canvas markerCanvas,
            TimeSpan? time,
            Brush color,
            double width,
            MarkerSelection type)
        {
            if (time == null)
                return;

            if (_duration <= 0)
                return;

            var sliderWidth =
                Slider.ActualWidth > 1
                    ? Slider.ActualWidth
                    : Slider.Width;

            if (sliderWidth <= 1)
            {
                sliderWidth =
                    ActualWidth > 1
                        ? ActualWidth - 20
                        : 300;
            }

            var ratio =
                Math.Clamp(
                    time.Value.TotalSeconds / _duration,
                    0,
                    1);

            var x =
                ratio *
                Math.Max(10, sliderWidth - 10);

            var isSelected =
                _selectedType == type &&
                _selectedMarkerTime.HasValue &&
                (_selectedMarkerTime.Value - time.Value)
                .Duration()
                <= TimeSpan.FromMilliseconds(1);

            var rect =
                new Rectangle
                {
                    Width = isSelected ? 6 : width,
                    Height = isSelected ? 24 : 18,
                    Fill = isSelected
                        ? Brushes.Gold
                        : color,

                    RadiusX = 1,
                    RadiusY = 1,

                    Stroke = isSelected
                        ? Brushes.Black
                        : Brushes.Transparent,

                    StrokeThickness =
                        isSelected ? 1 : 0,

                    Focusable = false,

                    Tag = "MARKER"
                };

            Canvas.SetLeft(
                rect,
                Math.Max(0, x));

            Canvas.SetTop(
                rect,
                isSelected ? 0 : 3);

            markerCanvas.Children.Add(rect);

            DrawMarkerLabel(
                markerCanvas,
                time.Value,
                x,
                isSelected);
        }

        private void DrawMarkerLabel(
            Canvas markerCanvas,
            TimeSpan time,
            double x,
            bool selected)
        {
            var totalFrames =
                Math.Max(
                    0,
                    (long)(time.TotalSeconds * 1000));

            var label =
                new TextBlock
                {
                    Text = totalFrames.ToString(),

                    Foreground =
                        selected
                            ? Brushes.Gold
                            : Brushes.White,

                    FontSize =
                        selected ? 12 : 10,

                    FontWeight =
                        selected
                            ? FontWeights.Bold
                            : FontWeights.Normal,

                    Focusable = false,

                    Tag = "MARKER"
                };

            Canvas.SetLeft(label, x + 4);
            Canvas.SetTop(label, 0);

            markerCanvas.Children.Add(label);
        }

        private void MarkerCanvas_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (sender is not Canvas markerCanvas)
                return;

            if (_duration <= 0)
                return;

            var p =
                e.GetPosition(markerCanvas);

            var sec =
                (p.X /
                 Math.Max(1,
                     markerCanvas.ActualWidth))
                * _duration;

            var click =
                TimeSpan.FromSeconds(sec);

            SelectNearestMarker(click);

            RefreshMarkers();
        }

        private void SelectNearestMarker(
            TimeSpan click)
        {
            _selectedType =
                MarkerSelection.None;

            _selectedMarkerTime =
                null;

            var markerCanvas =
                FindName("MarkerCanvas") as Canvas;

            if (markerCanvas == null)
                return;

            var thresholdPixels = 8.0;

            var thresholdSeconds =
                (_duration /
                 Math.Max(
                     1,
                     markerCanvas.ActualWidth))
                * thresholdPixels;

            var threshold =
                TimeSpan.FromSeconds(
                    thresholdSeconds);

            if (_inputMarker.HasValue &&
                (_inputMarker.Value - click)
                .Duration() <= threshold)
            {
                _selectedType =
                    MarkerSelection.Input;

                _selectedMarkerTime =
                    _inputMarker;

                return;
            }

            if (_outputMarker.HasValue &&
                (_outputMarker.Value - click)
                .Duration() <= threshold)
            {
                _selectedType =
                    MarkerSelection.Output;

                _selectedMarkerTime =
                    _outputMarker;

                return;
            }

            foreach (var cut in _cutMarkers)
            {
                if ((cut - click)
                    .Duration() <= threshold)
                {
                    _selectedType =
                        MarkerSelection.Cut;

                    _selectedMarkerTime =
                        cut;

                    return;
                }
            }
        }

        private void RefreshMarkers()
        {
            SetMarkers(
                _inputMarker,
                _outputMarker,
                _cutMarkers);
        }

        private void Slider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (_internalChange)
                return;

            if (_isDraggingSlider)
                return;

            OnChanged?.Invoke(
                TimeSpan.FromSeconds(
                    e.NewValue));
        }
    }
}