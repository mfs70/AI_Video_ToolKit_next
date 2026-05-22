// Файл: ViewModels/MarkersViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using AI_Video_ToolKit.UI.Messages;
using AI_Video_ToolKit.UI.ViewModels;

namespace AI_Video_ToolKit.UI.ViewModels
{
    /// <summary>
    /// Управляет маркерами (Input/Output/Cut), сегментами и Undo.
    /// </summary>
    public partial class MarkersViewModel : ObservableObject
    {
        private readonly IMessenger _messenger;
        private readonly PlaybackService _playback; // нужен для получения текущей позиции (можно через PositionChangedMessage, но пока упростим)

        private TimeSpan _inputMarker;
        private TimeSpan _outputMarker;
        private readonly List<TimeSpan> _cutMarkers = new();
        private readonly Stack<(MarkerActionType Type, TimeSpan Value, List<TimeSpan>? CutSnapshot)> _undoStack = new();

        private double _duration;
        private double _fps;

        public ObservableCollection<SegmentInfo> Segments { get; } = new();

        private SegmentInfo? _selectedSegment;
        public SegmentInfo? SelectedSegment
        {
            get => _selectedSegment;
            set => SetProperty(ref _selectedSegment, value);
        }

        public event Action? MarkersChanged;

        public MarkersViewModel(IMessenger messenger, PlaybackService playback)
        {
            _messenger = messenger;
            _playback = playback;

            // Подписываемся на события изменения позиции (чтобы знать, где ставить маркеры)
            // Но можно получать текущую позицию через _playback.CurrentPosition
            // Подпишемся на сообщение о загрузке нового файла
            _messenger.Register<FileLoadedMessage>(this, (r, m) =>
            {
                Reset(m.Duration, m.Fps);
            });
        }

        private void Reset(double duration, double fps)
        {
            _duration = duration;
            _fps = fps;
            _inputMarker = TimeSpan.Zero;
            _outputMarker = TimeSpan.Zero;
            _cutMarkers.Clear();
            _undoStack.Clear();
            RebuildSegments();
            MarkersChanged?.Invoke();
        }

        // Текущая позиция (берём из PlaybackService)
        private TimeSpan CurrentPosition => _playback.CurrentPosition;

        [RelayCommand]
        private void MarkInput()
        {
            var pos = CurrentPosition;
            _undoStack.Push((MarkerActionType.InputSet, _inputMarker, null));
            _inputMarker = pos;
            _cutMarkers.RemoveAll(c => c <= _inputMarker);
            RebuildSegments();
            MarkersChanged?.Invoke();
            _messenger.Send(new MarkersChangedMessage(_inputMarker, _outputMarker, _cutMarkers));
        }

        [RelayCommand]
        private void MarkOutput()
        {
            var pos = CurrentPosition;
            _undoStack.Push((MarkerActionType.OutputSet, _outputMarker, null));
            _outputMarker = pos;
            _cutMarkers.RemoveAll(c => c >= _outputMarker);
            RebuildSegments();
            MarkersChanged?.Invoke();
            _messenger.Send(new MarkersChangedMessage(_inputMarker, _outputMarker, _cutMarkers));
        }

        [RelayCommand]
        private void MarkCut()
        {
            var pos = CurrentPosition;
            if (_inputMarker != TimeSpan.Zero && pos <= _inputMarker) return;
            if (_outputMarker != TimeSpan.Zero && pos >= _outputMarker) return;
            _cutMarkers.Add(pos);
            _cutMarkers.Sort();
            _undoStack.Push((MarkerActionType.CutAdd, pos, null));
            RebuildSegments();
            MarkersChanged?.Invoke();
            _messenger.Send(new MarkersChangedMessage(_inputMarker, _outputMarker, _cutMarkers));
        }

        [RelayCommand]
        private void UndoMarker()
        {
            if (_undoStack.Count == 0) return;
            var action = _undoStack.Pop();
            switch (action.Type)
            {
                case MarkerActionType.InputSet:
                    _inputMarker = action.Value;
                    break;
                case MarkerActionType.OutputSet:
                    _outputMarker = action.Value;
                    break;
                case MarkerActionType.CutAdd:
                    if (action.Value != TimeSpan.Zero)
                        _cutMarkers.Remove(action.Value);
                    break;
                case MarkerActionType.CutClear:
                    _cutMarkers.Clear();
                    if (action.CutSnapshot != null)
                        _cutMarkers.AddRange(action.CutSnapshot);
                    break;
            }
            RebuildSegments();
            MarkersChanged?.Invoke();
            _messenger.Send(new MarkersChangedMessage(_inputMarker, _outputMarker, _cutMarkers));
        }

        [RelayCommand]
        private void ClearCuts()
        {
            if (_cutMarkers.Count == 0) return;
            _undoStack.Push((MarkerActionType.CutClear, TimeSpan.Zero, new List<TimeSpan>(_cutMarkers)));
            _cutMarkers.Clear();
            RebuildSegments();
            MarkersChanged?.Invoke();
            _messenger.Send(new MarkersChangedMessage(_inputMarker, _outputMarker, _cutMarkers));
        }

        public (double duration, TimeSpan? input, TimeSpan? output, IReadOnlyList<TimeSpan> cuts) GetTimelineData()
        {
            return (_duration,
                _inputMarker != TimeSpan.Zero ? _inputMarker : null,
                _outputMarker != TimeSpan.Zero ? _outputMarker : null,
                _cutMarkers);
        }

        public void MoveTimelineMarker(string markerType, TimeSpan? original, TimeSpan moved)
        {
            moved = ClampToMedia(SnapToFrame(moved));
            var frame = TimeSpan.FromSeconds(1 / Math.Max(0.0001, _fps));

            if (markerType == "Input")
            {
                if (_outputMarker != TimeSpan.Zero && moved >= _outputMarker)
                    moved = _outputMarker - frame;
                _inputMarker = ClampToMedia(moved);
                _cutMarkers.RemoveAll(c => c <= _inputMarker);
            }
            else if (markerType == "Output")
            {
                if (_inputMarker != TimeSpan.Zero && moved <= _inputMarker)
                    moved = _inputMarker + frame;
                _outputMarker = ClampToMedia(moved);
                _cutMarkers.RemoveAll(c => c >= _outputMarker);
            }
            else if (markerType == "Cut" && original.HasValue)
            {
                var index = _cutMarkers.FindIndex(c => c == original.Value);
                if (index < 0) return;

                var min = _inputMarker != TimeSpan.Zero ? _inputMarker + frame : frame;
                var max = _outputMarker != TimeSpan.Zero ? _outputMarker - frame : TimeSpan.FromSeconds(_duration) - frame;
                moved = TimeSpan.FromSeconds(Math.Clamp(moved.TotalSeconds, min.TotalSeconds, max.TotalSeconds));
                if (_cutMarkers.Where((_, i) => i != index).Any(c => (c - moved).Duration() < frame))
                    return;

                _cutMarkers[index] = moved;
                _cutMarkers.Sort();
            }

            RebuildSegments();
            MarkersChanged?.Invoke();
            _messenger.Send(new MarkersChangedMessage(_inputMarker, _outputMarker, _cutMarkers));
        }

        private void RebuildSegments()
        {
            Segments.Clear();
            if (_duration <= 0) return;
            var startBound = _inputMarker != TimeSpan.Zero ? _inputMarker : TimeSpan.Zero;
            var endBound = _outputMarker != TimeSpan.Zero ? _outputMarker : TimeSpan.FromSeconds(_duration);
            if (endBound <= startBound) return;
            var points = new List<TimeSpan> { startBound };
            points.AddRange(_cutMarkers.Where(c => c > startBound && c < endBound).OrderBy(x => x));
            points.Add(endBound);
            points = points.Distinct().OrderBy(x => x).ToList();
            int idx = 1;
            for (int i = 0; i < points.Count - 1; i++)
            {
                if (points[i + 1] <= points[i]) continue;
                Segments.Add(new SegmentInfo
                {
                    Index = idx++,
                    Start = points[i],
                    End = points[i + 1],
                    StartFrame = TimeToFrame(points[i]),
                    EndFrame = TimeToFrame(points[i + 1])
                });
            }
        }

        private long TimeToFrame(TimeSpan time) => (long)(time.TotalSeconds * _fps);
        private TimeSpan SnapToFrame(TimeSpan time) => TimeSpan.FromSeconds(Math.Round(time.TotalSeconds * _fps) / _fps);
        private TimeSpan ClampToMedia(TimeSpan time) => TimeSpan.FromSeconds(Math.Clamp(time.TotalSeconds, 0, Math.Max(0, _duration)));
    }

    // Вспомогательные классы (дублируем, чтобы не зависеть от MainViewModel)
    public class SegmentInfo
    {
        public int Index { get; set; }
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
        public long StartFrame { get; set; }
        public long EndFrame { get; set; }
        public TimeSpan Duration => End - Start;
        public override string ToString() => $"{Index:000}_{Start:hh\\:mm\\:ss\\.fff}_{End:hh\\:mm\\:ss\\.fff}";
    }

    internal enum MarkerActionType { InputSet, OutputSet, CutAdd, CutClear }
}