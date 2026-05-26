// Файл: ViewModels/MarkersViewModel.cs
// Описание: Управляет маркерами (Input/Output/Cut), сегментами и Undo/Redo.
// Отвечает за перестроение сегментов на основе маркеров и отправку уведомлений об изменениях.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using AI_Video_ToolKit.UI.Messages;
using AI_Video_ToolKit.UI.Services;

namespace AI_Video_ToolKit.UI.ViewModels
{
    /// <summary>
    /// Управляет маркерами (Input/Output/Cut), сегментами и Undo.
    /// </summary>
    public partial class MarkersViewModel : ObservableObject
    {
        private readonly IMessenger _messenger;      // Шина сообщений
        private readonly PlaybackService _playback;  // Сервис воспроизведения (для получения текущей позиции)

        // Текущие маркеры
        private TimeSpan _inputMarker;   // Маркер начала (I)
        private TimeSpan _outputMarker;  // Маркер конца (O)
        private readonly List<TimeSpan> _cutMarkers = new();  // Маркеры разреза (C)

        // Стек для Undo (сохраняет действия)
        private readonly Stack<(MarkerActionType Type, TimeSpan Value, List<TimeSpan>? CutSnapshot)> _undoStack = new();

        // Параметры текущего видео
        private double _duration;  // Длительность в секундах
        private double _fps;       // Кадров в секунду

        // Коллекция сегментов для отображения в UI
        public ObservableCollection<SegmentInfo> Segments { get; } = new();

        // Выбранный сегмент в UI
        private SegmentInfo? _selectedSegment;
        public SegmentInfo? SelectedSegment
        {
            get => _selectedSegment;
            set => SetProperty(ref _selectedSegment, value);
        }

        // Событие для уведомления об изменении маркеров (используется TimelineControl)
        public event Action? MarkersChanged;

        /// <summary>
        /// Конструктор. Получает зависимости через DI.
        /// </summary>
        public MarkersViewModel(IMessenger messenger, PlaybackService playback)
        {
            _messenger = messenger;
            _playback = playback;

            // Подписка на загрузку нового файла – сброс маркеров
            _messenger.Register<FileLoadedMessage>(this, (r, m) =>
            {
                Reset(m.Duration, m.Fps);
            });
        }

        /// <summary>
        /// Сброс всех маркеров и сегментов при загрузке нового файла.
        /// </summary>
        private void Reset(double duration, double fps)
        {
            _duration = duration;
            _fps = fps;
            _inputMarker = TimeSpan.Zero;
            _outputMarker = TimeSpan.Zero;
            _cutMarkers.Clear();
            _undoStack.Clear();
            RebuildSegments();
            _messenger.Send(new SegmentsChangedMessage(Segments.ToList()));
            MarkersChanged?.Invoke();
        }

        /// <summary>
        /// Текущая позиция воспроизведения (берётся из PlaybackService).
        /// </summary>
        private TimeSpan CurrentPosition => _playback.CurrentPosition;

        /// <summary>
        /// Установка маркера начала (I) в текущую позицию.
        /// </summary>
        [RelayCommand]
        private void MarkInput()
        {
            var pos = CurrentPosition;
            _undoStack.Push((MarkerActionType.InputSet, _inputMarker, null));
            _inputMarker = pos;
            _cutMarkers.RemoveAll(c => c <= _inputMarker);
            RebuildSegments();
            _messenger.Send(new SegmentsChangedMessage(Segments.ToList()));
            MarkersChanged?.Invoke();
            _messenger.Send(new MarkersChangedMessage(_inputMarker, _outputMarker, _cutMarkers));
        }

        /// <summary>
        /// Установка маркера конца (O) в текущую позицию.
        /// </summary>
        [RelayCommand]
        private void MarkOutput()
        {
            var pos = CurrentPosition;
            _undoStack.Push((MarkerActionType.OutputSet, _outputMarker, null));
            _outputMarker = pos;
            _cutMarkers.RemoveAll(c => c >= _outputMarker);
            RebuildSegments();
            _messenger.Send(new SegmentsChangedMessage(Segments.ToList()));
            MarkersChanged?.Invoke();
            _messenger.Send(new MarkersChangedMessage(_inputMarker, _outputMarker, _cutMarkers));
        }

        /// <summary>
        /// Установка маркера разреза (C) в текущую позицию.
        /// </summary>
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
            _messenger.Send(new SegmentsChangedMessage(Segments.ToList()));
            MarkersChanged?.Invoke();
            _messenger.Send(new MarkersChangedMessage(_inputMarker, _outputMarker, _cutMarkers));
        }

        /// <summary>
        /// Отмена последнего действия с маркерами.
        /// </summary>
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
            _messenger.Send(new SegmentsChangedMessage(Segments.ToList()));
            MarkersChanged?.Invoke();
            _messenger.Send(new MarkersChangedMessage(_inputMarker, _outputMarker, _cutMarkers));
        }

        /// <summary>
        /// Удаление всех маркеров разреза (очистка).
        /// </summary>
        [RelayCommand]
        private void ClearCuts()
        {
            if (_cutMarkers.Count == 0) return;
            _undoStack.Push((MarkerActionType.CutClear, TimeSpan.Zero, new List<TimeSpan>(_cutMarkers)));
            _cutMarkers.Clear();
            RebuildSegments();
            _messenger.Send(new SegmentsChangedMessage(Segments.ToList()));
            MarkersChanged?.Invoke();
            _messenger.Send(new MarkersChangedMessage(_inputMarker, _outputMarker, _cutMarkers));
        }

        /// <summary>
        /// Возвращает данные для отображения на таймлайне.
        /// </summary>
        public (double duration, TimeSpan? input, TimeSpan? output, IReadOnlyList<TimeSpan> cuts) GetTimelineData()
        {
            return (_duration,
                _inputMarker != TimeSpan.Zero ? _inputMarker : null,
                _outputMarker != TimeSpan.Zero ? _outputMarker : null,
                _cutMarkers);
        }

        /// <summary>
        /// Перемещение маркера на таймлайне (вызывается из TimelineControl).
        /// </summary>
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
            _messenger.Send(new SegmentsChangedMessage(Segments.ToList()));
            MarkersChanged?.Invoke();
            _messenger.Send(new MarkersChangedMessage(_inputMarker, _outputMarker, _cutMarkers));
        }

        /// <summary>
        /// Перестраивает список сегментов на основе текущих маркеров.
        /// Вызывается после любого изменения маркеров.
        /// </summary>
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

            // Отправляем диагностическое сообщение в лог (будет видно в панели Logger)
            _messenger.Send(new PingMessage($"RebuildSegments: Segments count = {Segments.Count}"));

            // Уведомляем ExportViewModel об изменении сегментов (это ключевая строка!)
            _messenger.Send(new SegmentsChangedMessage(Segments.ToList()));
        }

        // Вспомогательные методы для работы с кадрами
        private long TimeToFrame(TimeSpan time) => (long)(time.TotalSeconds * _fps);
        private TimeSpan SnapToFrame(TimeSpan time) => TimeSpan.FromSeconds(Math.Round(time.TotalSeconds * _fps) / _fps);
        private TimeSpan ClampToMedia(TimeSpan time) => TimeSpan.FromSeconds(Math.Clamp(time.TotalSeconds, 0, Math.Max(0, _duration)));
    }
}