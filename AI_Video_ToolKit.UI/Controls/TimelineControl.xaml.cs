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
        private const double MarkerHitRadiusPixels = 10;

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
        private bool _isPlayheadSelected;

        public event Action<TimeSpan>? OnChanged;
        public event Action<TimeSpan>? PreviewRequested;
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
            Canvas.SetTop(InputMarkerLabel, 0);
            Canvas.SetTop(OutputMarkerLabel, 0);
            Canvas.SetTop(InputMarker, 16);
            Canvas.SetTop(OutputMarker, 16);
            Canvas.SetTop(PlayheadLine, 16);
        }

        public MarkerSelection SelectedMarkerType => _selectedType;
        public TimeSpan? SelectedMarkerTime => _selectedMarkerTime;
        public bool HasSelectedMarker => _selectedType != MarkerSelection.None && _selectedMarkerTime.HasValue;
        public bool IsPlayheadSelected => _isPlayheadSelected;

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
                PlayheadLine.Visibility = Visibility.Collapsed;
                return;
            }

            FrameCursor.Visibility = Visibility.Visible;
            PlayheadLine.Visibility = Visibility.Visible;
            FrameText.Text = frame.ToString();
            UpdateFrameCursorLayout();
        }

        public void SetMarkers(TimeSpan? input, TimeSpan? output, IReadOnlyCollection<TimeSpan> cuts)
        {
            _inputMarker = input;
            _outputMarker = output;

            InputMarker.Visibility = input.HasValue ? Visibility.Visible : Visibility.Collapsed;
            InputMarkerLabel.Visibility = input.HasValue ? Visibility.Visible : Visibility.Collapsed;
            OutputMarker.Visibility = output.HasValue ? Visibility.Visible : Visibility.Collapsed;
            OutputMarkerLabel.Visibility = output.HasValue ? Visibility.Visible : Visibility.Collapsed;
            EnsureCutMarkerCount(cuts.Count);

            var orderedCuts = cuts.OrderBy(x => x).ToArray();
            for (var i = 0; i < _cutMarkers.Count; i++)
            {
                var visual = _cutMarkers[i];
                visual.Time = i < orderedCuts.Length ? orderedCuts[i] : null;
                visual.Shape.Visibility = visual.Time.HasValue ? Visibility.Visible : Visibility.Collapsed;
                visual.Label.Visibility = visual.Time.HasValue ? Visibility.Visible : Visibility.Collapsed;
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
            SelectNearestMarkerAtX(position.X);

            if (_selectedType == MarkerSelection.None)
            {
                _isPlayheadSelected = true;
                ApplySelectionVisualState();
                _isPlayheadDragging = true;
                QueuePlayheadSeek(time);
                MarkerCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }
            _isPlayheadSelected = false;
            if (_selectedMarkerTime.HasValue)
                PreviewRequested?.Invoke(_selectedMarkerTime.Value);
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
            if (e.Key == Key.Escape)
            {
                ClearSelection();
                e.Handled = true;
                return;
            }

            if (_isPlayheadSelected && TryMovePlayheadByKey(e.Key, Keyboard.Modifiers))
            {
                e.Handled = true;
                return;
            }

            if (!HasSelectedMarker) return;
            if (TryMoveSelectedMarkerByKey(e.Key, Keyboard.Modifiers))
                e.Handled = true;
        }

        public bool TryMovePlayheadByKey(Key key, ModifierKeys modifiers)
        {
            if (key != Key.Left && key != Key.Right) return false;

            _isPlayheadSelected = true;
            _selectedType = MarkerSelection.None;
            _selectedMarkerTime = null;
            ApplySelectionVisualState();

            var frameStep = modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            var delta = TimeSpan.FromSeconds(frameStep / _fps);
            var current = TimeSpan.FromSeconds(Slider.Value);
            var target = key == Key.Left ? current - delta : current + delta;
            QueuePlayheadSeek(ClampToDuration(SnapToFrame(target)));
            FlushPendingSliderChange();
            return true;
        }

        public bool TryMoveSelectedMarkerByKey(Key key, ModifierKeys modifiers)
        {
            if (!HasSelectedMarker) return false;
            if (key != Key.Left && key != Key.Right) return false;

            var frameStep = modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;

            var delta = TimeSpan.FromSeconds(frameStep / _fps);
            var original = _selectedMarkerTime.GetValueOrDefault();
            var target = key == Key.Left ? original - delta : original + delta;

            MoveSelectedMarker(target);
            MarkerMoved?.Invoke(_selectedType, original, _selectedMarkerTime.GetValueOrDefault(original));
            return true;
        }

        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateMarkerLayout();

        private void EnsureCutMarkerCount(int count)
        {
            while (_cutMarkers.Count < count)
            {
                var shape = new Rectangle
                {
                    Width = 2,
                    Height = 22,
                    Fill = _cutBrush,
                    Cursor = Cursors.SizeWE,
                    Visibility = Visibility.Collapsed
                };
                var label = new TextBlock
                {
                    Foreground = _cutBrush,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Visibility = Visibility.Collapsed
                };
                Canvas.SetTop(label, 0);
                Canvas.SetTop(shape, 16);
                CutMarkerLayer.Children.Add(label);
                CutMarkerLayer.Children.Add(shape);
                _cutMarkers.Add(new CutMarkerVisual(shape, label));
            }
        }

        private void SelectNearestMarkerAtX(double clickX)
        {
            _selectedType = MarkerSelection.None;
            _selectedMarkerTime = null;
            _isPlayheadSelected = false;

            var nearestType = MarkerSelection.None;
            TimeSpan? nearestTime = null;
            var nearestDistance = double.MaxValue;

            void Consider(MarkerSelection type, TimeSpan? time)
            {
                if (!time.HasValue) return;
                var distance = Math.Abs(TimeToX(time.Value) - clickX);
                if (distance >= nearestDistance) return;
                nearestDistance = distance;
                nearestType = type;
                nearestTime = time;
            }

            Consider(MarkerSelection.Input, _inputMarker);
            Consider(MarkerSelection.Output, _outputMarker);
            foreach (var marker in _cutMarkers.Where(x => x.Time.HasValue))
                Consider(MarkerSelection.Cut, marker.Time);

            if (nearestTime.HasValue && nearestDistance <= MarkerHitRadiusPixels)
            {
                _selectedType = nearestType;
                _selectedMarkerTime = nearestTime;
            }
            else
            {
                _isPlayheadSelected = true;
            }

            ApplySelectionVisualState();
        }

        public void ClearSelection()
        {
            _selectedType = MarkerSelection.None;
            _selectedMarkerTime = null;
            _dragType = MarkerSelection.None;
            _dragOriginalTime = null;
            _isPlayheadSelected = false;
            _isPlayheadDragging = false;
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
            PreviewRequested?.Invoke(moved);
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
            if (_inputMarker.HasValue)
                SetMarkerLayout(InputMarker, InputMarkerLabel, _inputMarker.Value);
            if (_outputMarker.HasValue)
                SetMarkerLayout(OutputMarker, OutputMarkerLabel, _outputMarker.Value);
            foreach (var marker in _cutMarkers.Where(x => x.Time.HasValue))
                SetMarkerLayout(marker.Shape, marker.Label, marker.Time!.Value);
            UpdateFrameCursorLayout();
        }

        private void SetMarkerLayout(FrameworkElement marker, TextBlock label, TimeSpan time)
        {
            var x = TimeToX(time);
            Canvas.SetLeft(marker, Math.Max(0, x - marker.Width / 2));

            label.Text = TimeToFrame(time).ToString();
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var labelWidth = label.DesiredSize.Width > 1 ? label.DesiredSize.Width : 24;
            Canvas.SetLeft(label, Math.Clamp(x - labelWidth / 2, 0, Math.Max(0, MarkerCanvas.ActualWidth - labelWidth)));
        }

        private void UpdateFrameCursorLayout()
        {
            if (FrameCursor.Visibility != Visibility.Visible) return;
            var x = TimeToX(TimeSpan.FromSeconds(Slider.Value));
            Canvas.SetLeft(PlayheadLine, Math.Clamp(x - PlayheadLine.Width / 2, 0, Math.Max(0, MarkerCanvas.ActualWidth - PlayheadLine.Width)));
            var cursorWidth = FrameCursor.ActualWidth > 1 ? FrameCursor.ActualWidth : FrameCursor.MinWidth;
            var maxLeft = Math.Max(0, MarkerCanvas.ActualWidth - cursorWidth);
            Canvas.SetLeft(FrameCursor, Math.Clamp(x - cursorWidth / 2, 0, maxLeft));
            Canvas.SetTop(FrameCursor, 38 - FrameCursor.ActualHeight / 2);
        }

        private void ApplySelectionVisualState()
        {
            InputMarker.Fill = _selectedType == MarkerSelection.Input ? _selectedBrush : _inputBrush;
            InputMarkerLabel.Foreground = _selectedType == MarkerSelection.Input ? _selectedBrush : _inputBrush;
            OutputMarker.Fill = _selectedType == MarkerSelection.Output ? _selectedBrush : _outputBrush;
            OutputMarkerLabel.Foreground = _selectedType == MarkerSelection.Output ? _selectedBrush : _outputBrush;
            PlayheadLine.Fill = _isPlayheadSelected ? _selectedBrush : Brushes.LightGreen;
            FrameCursor.BorderBrush = _isPlayheadSelected ? _selectedBrush : Brushes.LightGreen;
            FrameText.Foreground = _isPlayheadSelected ? _selectedBrush : Brushes.LightGreen;
            foreach (var marker in _cutMarkers)
            {
                var brush = marker.Time.HasValue && _selectedType == MarkerSelection.Cut && marker.Time == _selectedMarkerTime
                    ? _selectedBrush
                    : _cutBrush;
                marker.Shape.Fill = brush;
                marker.Label.Foreground = brush;
            }
        }

        private TimeSpan SnapToFrame(TimeSpan time)
        {
            var frame = 1 / _fps;
            return TimeSpan.FromSeconds(Math.Round(time.TotalSeconds / frame) * frame);
        }

        private long TimeToFrame(TimeSpan time) => (long)Math.Round(time.TotalSeconds * _fps);

        private TimeSpan ClampToDuration(TimeSpan time)
        {
            return TimeSpan.FromSeconds(Math.Clamp(time.TotalSeconds, 0, Math.Max(0, _duration)));
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
            public CutMarkerVisual(Rectangle shape, TextBlock label)
            {
                Shape = shape;
                Label = label;
            }

            public Rectangle Shape { get; }
            public TextBlock Label { get; }
            public TimeSpan? Time { get; set; }
        }
    }
}
