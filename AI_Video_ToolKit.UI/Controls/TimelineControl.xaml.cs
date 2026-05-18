using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AI_Video_ToolKit.UI.Controls
{
    public sealed partial class TimelineControl : UserControl
    {
        public enum MarkerSelection { None, Input, Output, Cut }

        private readonly DispatcherTimer _sliderThrottle;
        private readonly List<CutMarkerVisual> _cutMarkers = new();
        private readonly Brush _cutBrush = Brushes.White;
        private readonly Brush _selectedBrush = Brushes.Gold;
        private readonly Brush _inputBrush = Brushes.LimeGreen;
        private readonly Brush _outputBrush = Brushes.IndianRed;

        private double _duration;
        private double _fps = 25;
        private bool _internalChange;
        private bool _pendingSliderChange;
        private bool _isUserSliderInteraction;
        private TimeSpan _pendingSliderTime;
        private TimeSpan? _inputMarker;
        private TimeSpan? _outputMarker;
        private MarkerSelection _selectedType = MarkerSelection.None;
        private TimeSpan? _selectedMarkerTime;
        private MarkerSelection _dragType = MarkerSelection.None;
        private TimeSpan? _dragOriginalTime;
        private bool _isPlayheadDragging;

        public event Action<TimeSpan>? OnChanged;
        public event Action<MarkerSelection, TimeSpan?, TimeSpan>? MarkerMoved;

        public TimelineControl()
        {
            InitializeComponent();
            _sliderThrottle = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _sliderThrottle.Tick += SliderThrottle_Tick;
            Slider.PreviewMouseLeftButtonDown += (_, _) => _isUserSliderInteraction = true;
            Slider.PreviewMouseLeftButtonUp += (_, _) =>
            {
                _isUserSliderInteraction = false;
                FlushPendingSliderChange();
            };
            Slider.LostMouseCapture += (_, _) => _isUserSliderInteraction = false;
            Canvas.SetTop(InputMarker, 2);
            Canvas.SetTop(OutputMarker, 2);
        }

        public MarkerSelection SelectedMarkerType => _selectedType;
        public TimeSpan? SelectedMarkerTime => _selectedMarkerTime;

        public void SetDuration(double duration)
        {
            _duration = Math.Max(0, duration);
            Slider.Maximum = _duration;
            UpdateMarkerLayout();
        }

        public void SetFrameRate(double fps)
        {
            _fps = fps > 0 ? fps : 25;
        }

        public void SetCurrentTime(TimeSpan time)
        {
            _internalChange = true;
            Slider.Value = Math.Clamp(time.TotalSeconds, 0, Slider.Maximum);
            TimeText.Text = $"{time:mm\\:ss} / {TimeSpan.FromSeconds(_duration):mm\\:ss}";
            _internalChange = false;
            UpdateFrameCursorLayout();
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
            UpdateFrameCursorLayout();
        }

        public void SetMarkers(TimeSpan? input, TimeSpan? output, IReadOnlyCollection<TimeSpan> cuts)
        {
            _inputMarker = input;
            _outputMarker = output;

            InputMarker.Visibility = input.HasValue ? Visibility.Visible : Visibility.Collapsed;
            OutputMarker.Visibility = output.HasValue ? Visibility.Visible : Visibility.Collapsed;
            EnsureCutMarkerCount(cuts.Count);

            var orderedCuts = cuts.OrderBy(x => x).ToArray();
            for (var i = 0; i < _cutMarkers.Count; i++)
            {
                var visual = _cutMarkers[i];
                visual.Time = i < orderedCuts.Length ? orderedCuts[i] : null;
                visual.Shape.Visibility = visual.Time.HasValue ? Visibility.Visible : Visibility.Collapsed;
            }

            UpdateMarkerLayout();
            ApplySelectionVisualState();
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_internalChange) return;
            if (!IsUserSliderInput()) return;

            _pendingSliderTime = SnapToFrame(TimeSpan.FromSeconds(e.NewValue));
            _pendingSliderChange = true;
            if (!_sliderThrottle.IsEnabled)
                _sliderThrottle.Start();
        }

        private bool IsUserSliderInput()
        {
            return _isUserSliderInteraction ||
                   Slider.IsMouseCaptureWithin ||
                   (Slider.IsKeyboardFocusWithin &&
                    (Keyboard.IsKeyDown(Key.Left) ||
                     Keyboard.IsKeyDown(Key.Right) ||
                     Keyboard.IsKeyDown(Key.Home) ||
                     Keyboard.IsKeyDown(Key.End) ||
                     Keyboard.IsKeyDown(Key.PageUp) ||
                     Keyboard.IsKeyDown(Key.PageDown)));
        }

        private void SliderThrottle_Tick(object? sender, EventArgs e)
        {
            if (!_pendingSliderChange)
            {
                _sliderThrottle.Stop();
                return;
            }

            FlushPendingSliderChange();
        }

        private void FlushPendingSliderChange()
        {
            if (!_pendingSliderChange) return;
            _pendingSliderChange = false;
            OnChanged?.Invoke(_pendingSliderTime);
        }

        private void MarkerCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Focus();
            var position = e.GetPosition(MarkerCanvas);
            var time = XToTime(position.X);
            SelectNearestMarker(time);

            if (_selectedType == MarkerSelection.None)
            {
                _isPlayheadDragging = true;
                QueuePlayheadSeek(time);
                MarkerCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }
            _dragType = _selectedType;
            _dragOriginalTime = _selectedMarkerTime;
            MarkerCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void MarkerCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPlayheadDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                QueuePlayheadSeek(XToTime(e.GetPosition(MarkerCanvas).X));
                e.Handled = true;
                return;
            }

            if (_dragType == MarkerSelection.None || e.LeftButton != MouseButtonState.Pressed) return;
            var time = XToTime(e.GetPosition(MarkerCanvas).X);
            MoveSelectedMarker(time);
            e.Handled = true;
        }

        private void MarkerCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPlayheadDragging)
            {
                QueuePlayheadSeek(XToTime(e.GetPosition(MarkerCanvas).X));
                FlushPendingSliderChange();
                _isPlayheadDragging = false;
                MarkerCanvas.ReleaseMouseCapture();
                e.Handled = true;
                return;
            }

            if (_dragType == MarkerSelection.None) return;
            MarkerCanvas.ReleaseMouseCapture();
            MarkerMoved?.Invoke(_dragType, _dragOriginalTime, _selectedMarkerTime ?? TimeSpan.Zero);
            _dragType = MarkerSelection.None;
            _dragOriginalTime = null;
            e.Handled = true;
        }

        private void QueuePlayheadSeek(TimeSpan time)
        {
            _pendingSliderTime = time;
            _pendingSliderChange = true;
            SetCurrentTime(time);
            if (!_sliderThrottle.IsEnabled)
                _sliderThrottle.Start();
        }

        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_selectedType == MarkerSelection.None || !_selectedMarkerTime.HasValue) return;
            var frameStep = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            var delta = TimeSpan.FromSeconds(frameStep / _fps);
            if (e.Key == Key.Left)
            {
                var original = _selectedMarkerTime;
                MoveSelectedMarker(_selectedMarkerTime.Value - delta);
                MarkerMoved?.Invoke(_selectedType, original, _selectedMarkerTime.Value);
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                var original = _selectedMarkerTime;
                MoveSelectedMarker(_selectedMarkerTime.Value + delta);
                MarkerMoved?.Invoke(_selectedType, original, _selectedMarkerTime.Value);
                e.Handled = true;
            }
        }

        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateMarkerLayout();

        private void EnsureCutMarkerCount(int count)
        {
            while (_cutMarkers.Count < count)
            {
                var shape = new Rectangle
                {
                    Width = 2,
                    Height = 18,
                    Fill = _cutBrush,
                    Cursor = Cursors.SizeWE,
                    Visibility = Visibility.Collapsed
                };
                Canvas.SetTop(shape, 2);
                CutMarkerLayer.Children.Add(shape);
                _cutMarkers.Add(new CutMarkerVisual(shape));
            }
        }

        private void SelectNearestMarker(TimeSpan click)
        {
            _selectedType = MarkerSelection.None;
            _selectedMarkerTime = null;
            var threshold = TimeSpan.FromSeconds(Math.Max(1 / _fps, 0.2));

            if (_inputMarker.HasValue && (_inputMarker.Value - click).Duration() <= threshold)
            {
                _selectedType = MarkerSelection.Input;
                _selectedMarkerTime = _inputMarker;
            }
            else if (_outputMarker.HasValue && (_outputMarker.Value - click).Duration() <= threshold)
            {
                _selectedType = MarkerSelection.Output;
                _selectedMarkerTime = _outputMarker;
            }
            else
            {
                var cut = _cutMarkers
                    .Where(x => x.Time.HasValue)
                    .Select(x => x.Time!.Value)
                    .OrderBy(x => (x - click).Duration())
                    .FirstOrDefault();
                if (cut != default && (cut - click).Duration() <= threshold)
                {
                    _selectedType = MarkerSelection.Cut;
                    _selectedMarkerTime = cut;
                }
            }

            ApplySelectionVisualState();
        }

        private void MoveSelectedMarker(TimeSpan requested)
        {
            var previous = _selectedMarkerTime;
            var moved = ClampMarker(_selectedType, _selectedMarkerTime, SnapToFrame(requested));
            _selectedMarkerTime = moved;
            if (_selectedType == MarkerSelection.Input) _inputMarker = moved;
            if (_selectedType == MarkerSelection.Output) _outputMarker = moved;
            if (_selectedType == MarkerSelection.Cut)
            {
                // Cut markers keep persistent visual objects; we update the existing
                // rectangle instead of recreating marker visuals during interaction.
                var visual = _cutMarkers.FirstOrDefault(x => x.Time == _dragOriginalTime || x.Time == previous);
                if (visual != null) visual.Time = moved;
            }
            UpdateMarkerLayout();
            ApplySelectionVisualState();
        }

        private TimeSpan ClampMarker(MarkerSelection type, TimeSpan? original, TimeSpan requested)
        {
            var frame = TimeSpan.FromSeconds(1 / _fps);
            var min = TimeSpan.Zero;
            var max = TimeSpan.FromSeconds(_duration);
            if (type == MarkerSelection.Input && _outputMarker.HasValue) max = _outputMarker.Value - frame;
            if (type == MarkerSelection.Output && _inputMarker.HasValue) min = _inputMarker.Value + frame;
            if (type == MarkerSelection.Cut)
            {
                if (_inputMarker.HasValue) min = _inputMarker.Value + frame;
                if (_outputMarker.HasValue) max = _outputMarker.Value - frame;
                foreach (var cut in _cutMarkers.Where(x => x.Time.HasValue && x.Time != original).Select(x => x.Time!.Value))
                {
                    if ((cut - requested).Duration() < frame)
                        requested = requested < cut ? cut - frame : cut + frame;
                }
            }

            if (max < min) max = min;
            return TimeSpan.FromSeconds(Math.Clamp(requested.TotalSeconds, min.TotalSeconds, max.TotalSeconds));
        }

        private void UpdateMarkerLayout()
        {
            if (_duration <= 0) return;
            if (_inputMarker.HasValue) Canvas.SetLeft(InputMarker, TimeToX(_inputMarker.Value));
            if (_outputMarker.HasValue) Canvas.SetLeft(OutputMarker, TimeToX(_outputMarker.Value));
            foreach (var marker in _cutMarkers.Where(x => x.Time.HasValue))
                Canvas.SetLeft(marker.Shape, TimeToX(marker.Time!.Value));
            UpdateFrameCursorLayout();
        }

        private void UpdateFrameCursorLayout()
        {
            if (FrameCursor.Visibility != Visibility.Visible) return;
            var x = TimeToX(TimeSpan.FromSeconds(Slider.Value));
            var cursorWidth = FrameCursor.ActualWidth > 1 ? FrameCursor.ActualWidth : FrameCursor.MinWidth;
            var maxLeft = Math.Max(0, MarkerCanvas.ActualWidth - cursorWidth);
            Canvas.SetLeft(FrameCursor, Math.Clamp(x - cursorWidth / 2, 0, maxLeft));
            Canvas.SetTop(FrameCursor, 0);
        }

        private void ApplySelectionVisualState()
        {
            InputMarker.Fill = _selectedType == MarkerSelection.Input ? _selectedBrush : _inputBrush;
            OutputMarker.Fill = _selectedType == MarkerSelection.Output ? _selectedBrush : _outputBrush;
            foreach (var marker in _cutMarkers)
                marker.Shape.Fill = marker.Time.HasValue && _selectedType == MarkerSelection.Cut && marker.Time == _selectedMarkerTime
                    ? _selectedBrush
                    : _cutBrush;
        }

        private TimeSpan SnapToFrame(TimeSpan time)
        {
            var frame = 1 / _fps;
            return TimeSpan.FromSeconds(Math.Round(time.TotalSeconds / frame) * frame);
        }

        private double TimeToX(TimeSpan time)
        {
            var width = Math.Max(1, MarkerCanvas.ActualWidth);
            return Math.Clamp(time.TotalSeconds / Math.Max(_duration, 0.0001), 0, 1) * width;
        }

        private TimeSpan XToTime(double x)
        {
            var width = Math.Max(1, MarkerCanvas.ActualWidth);
            return SnapToFrame(TimeSpan.FromSeconds(Math.Clamp(x / width, 0, 1) * _duration));
        }

        private sealed class CutMarkerVisual
        {
            public CutMarkerVisual(Rectangle shape) => Shape = shape;
            public Rectangle Shape { get; }
            public TimeSpan? Time { get; set; }
        }
    }
}
