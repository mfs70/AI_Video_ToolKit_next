// Файл: ViewModels/MainViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using AI_Video_ToolKit.Infrastructure.Services;
using AI_Video_ToolKit.UI.Services;

namespace AI_Video_ToolKit.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        // Сервисы
        private readonly FFprobeService _ffprobe;
        private readonly FFmpegProcessService _ffmpeg;
        private readonly PlaybackService _playback;
        private readonly FrameGrabber _grabber;

        // Состояние
        [ObservableProperty] private string _statusText = "✅ Ready";
        [ObservableProperty] private bool _isPlaying;

        // Метаданные текущего файла
        [ObservableProperty] private string _currentFile = "";
        [ObservableProperty] private string _currentFileName = "";
        [ObservableProperty] private string _resolution = "";
        [ObservableProperty] private string _fpsStr = "";
        [ObservableProperty] private string _codec = "";
        [ObservableProperty] private string _bitrate = "";
        [ObservableProperty] private string _duration = "";
        [ObservableProperty] private string _audioInfo = "";

        // Позиция
        [ObservableProperty] private TimeSpan _currentPosition;
        [ObservableProperty] private long _currentFrame;
        [ObservableProperty] private long _totalFrames;
        public string CurrentTimeStr => CurrentPosition.ToString(@"hh\:mm\:ss");
        public string TotalTimeStr => _fileDurationSec > 0
            ? TimeSpan.FromSeconds(_fileDurationSec).ToString(@"hh\:mm\:ss")
            : "00:00:00";

        private double _fileDurationSec;
        private double _fileFps = 25;
        private long _videoBitrate;
        private bool _hasAudio;

        // Маркеры и сегменты
        private TimeSpan _inputMarker;
        private TimeSpan _outputMarker;
        private readonly List<TimeSpan> _cutMarkers = new();
        private readonly Stack<(MarkerActionType Type, TimeSpan Value, List<TimeSpan> CutSnapshot)> _undoStack = new();
        public ObservableCollection<SegmentInfo> Segments { get; } = new();
        [ObservableProperty] private SegmentInfo? _selectedSegment;
        public event Action? MarkersChanged;

        // Плейлист и монтажный стол
        public ObservableCollection<PlaylistItem> PlaylistItems { get; } = new();
        public ObservableCollection<MontageItem> MontageItems { get; } = new();
        [ObservableProperty] private PlaylistItem? _selectedPlaylistItem;

        // Скорость
        private readonly double[] _speeds = { 0.1, 0.25, 0.5, 1, 2, 4, 8, 16 };
        private int _speedIndex = 3;
        [ObservableProperty] private int _selectedSpeedIndex = 3;
        public double Speed => _speeds[_speedIndex];
        partial void OnSelectedSpeedIndexChanged(int value)
        {
            if (value >= 0 && value < _speeds.Length)
            {
                _speedIndex = value;
                OnPropertyChanged(nameof(Speed));
                _playback.SetSpeed(Speed);
            }
        }

        // Конструктор
        public MainViewModel(FFprobeService ffprobe, FFmpegProcessService ffmpeg,
            PlaybackService playback, FrameGrabber grabber)
        {
            _ffprobe = ffprobe; _ffmpeg = ffmpeg; _playback = playback; _grabber = grabber;
            _playback.OnFrameChanged += _ => { };
            _playback.OnPositionChanged += pos =>
            {
                CurrentPosition = pos;
                CurrentFrame = TimeToFrame(pos);
                OnPropertyChanged(nameof(CurrentTimeStr));
            };
            _playback.OnPlaybackEnded += () =>
            {
                IsPlaying = false;
                StatusText = "⏸ Paused";
            };
        }

        // Команды управления файлами и плеером
        [RelayCommand]
        private async Task LoadFiles()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Media files|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.jpg;*.jpeg;*.png;*.bmp;*.gif",
                Multiselect = true
            };
            if (dlg.ShowDialog() == true)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var path in dlg.FileNames) AddToPlaylist(path);
                });
                if (PlaylistItems.Count > 0 && string.IsNullOrEmpty(CurrentFile))
                    await LoadFile(PlaylistItems[0].FilePath);
            }
        }

        [RelayCommand] private async Task ClearPlaylist() { PlaylistItems.Clear(); Segments.Clear(); MarkersChanged?.Invoke(); await Task.CompletedTask; }
        [RelayCommand] private async Task RemoveSelectedFromPlaylist() { if (SelectedPlaylistItem != null) PlaylistItems.Remove(SelectedPlaylistItem); await Task.CompletedTask; }

        [RelayCommand]
        private async Task PlayPause()
        {
            if (string.IsNullOrEmpty(CurrentFile))
            {
                if (PlaylistItems.Count > 0) await LoadFile(PlaylistItems[0].FilePath);
                return;
            }
            if (_playback.IsPlaying) { _playback.Pause(); IsPlaying = false; StatusText = "⏸ Paused"; }
            else { _playback.Resume(); IsPlaying = true; StatusText = "▶ Playing"; }
            await Task.CompletedTask;
        }

        [RelayCommand]
        private async Task Stop()
        {
            _playback.Stop(); IsPlaying = false; StatusText = "⏹ Stopped";
            CurrentPosition = TimeSpan.Zero; CurrentFrame = 0;
            OnPropertyChanged(nameof(CurrentTimeStr));
            await Task.CompletedTask;
        }

        [RelayCommand]
        private async Task Next()
        {
            if (PlaylistItems.Count == 0) return;
            int idx = PlaylistItems.IndexOf(SelectedPlaylistItem!);
            if (idx < 0 || idx >= PlaylistItems.Count - 1) idx = 0; else idx++;
            await LoadFile(PlaylistItems[idx].FilePath);
        }

        [RelayCommand]
        private async Task Previous()
        {
            if (PlaylistItems.Count == 0) return;
            int idx = PlaylistItems.IndexOf(SelectedPlaylistItem!);
            if (idx <= 0) idx = PlaylistItems.Count - 1; else idx--;
            await LoadFile(PlaylistItems[idx].FilePath);
        }

        // Маркеры
        [RelayCommand] private void MarkInput() { _undoStack.Push((MarkerActionType.InputSet, _inputMarker, null!)); _inputMarker = CurrentPosition; _cutMarkers.RemoveAll(c => c <= _inputMarker); RebuildSegments(); MarkersChanged?.Invoke(); }
        [RelayCommand] private void MarkOutput() { _undoStack.Push((MarkerActionType.OutputSet, _outputMarker, null!)); _outputMarker = CurrentPosition; _cutMarkers.RemoveAll(c => c >= _outputMarker); RebuildSegments(); MarkersChanged?.Invoke(); }
        [RelayCommand]
        private void MarkCut()
        {
            var pos = CurrentPosition;
            if (_inputMarker != TimeSpan.Zero && pos <= _inputMarker) return;
            if (_outputMarker != TimeSpan.Zero && pos >= _outputMarker) return;
            _cutMarkers.Add(pos); _cutMarkers.Sort();
            _undoStack.Push((MarkerActionType.CutAdd, pos, null!));
            RebuildSegments(); MarkersChanged?.Invoke();
        }
        [RelayCommand]
        private void UndoMarker()
        {
            if (_undoStack.Count == 0) return;
            var action = _undoStack.Pop();
            switch (action.Type)
            {
                case MarkerActionType.InputSet: _inputMarker = action.Value; break;
                case MarkerActionType.OutputSet: _outputMarker = action.Value; break;
                case MarkerActionType.CutAdd: if (action.Value != TimeSpan.Zero) _cutMarkers.Remove(action.Value); break;
                case MarkerActionType.CutClear: _cutMarkers.Clear(); if (action.CutSnapshot != null) _cutMarkers.AddRange(action.CutSnapshot); break;
            }
            RebuildSegments(); MarkersChanged?.Invoke();
        }
        [RelayCommand] private void ClearCuts() { if (_cutMarkers.Count == 0) return; _undoStack.Push((MarkerActionType.CutClear, TimeSpan.Zero, new List<TimeSpan>(_cutMarkers))); _cutMarkers.Clear(); RebuildSegments(); MarkersChanged?.Invoke(); }

        // Предпросмотр и экспорт
        [RelayCommand]
        private async Task PreviewSegment()
        {
            if (SelectedSegment == null) return;
            _playback.Stop();
            _playback.Start(CurrentFile, _fileFps, SelectedSegment.Start, Speed, _hasAudio);
            IsPlaying = true; StatusText = "▶ Preview Segment";
            await Task.CompletedTask;
        }

        [RelayCommand] private async Task ExportSelected() { if (SelectedSegment != null) await ExportSegment(SelectedSegment); }
        [RelayCommand] private async Task ExportAll() { foreach (var seg in Segments) await ExportSegment(seg); }
        private async Task ExportSegment(SegmentInfo seg)
        {
            if (string.IsNullOrEmpty(CurrentFile)) return;
            var root = Directory.GetCurrentDirectory();
            var cutDir = Path.Combine(root, "Cut");
            Directory.CreateDirectory(cutDir);
            var srcName = Path.GetFileNameWithoutExtension(CurrentFile);
            var ext = Path.GetExtension(CurrentFile);
            var outFile = Path.Combine(cutDir, $"{seg.Index:000}_{srcName}_{seg.StartFrame}_{seg.EndFrame}{ext}");
            var startTime = seg.Start.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var endTime = seg.End.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var bitrateKbps = Math.Max(1500, (int)((_videoBitrate > 0 ? _videoBitrate : 4_000_000) / 1000));
            var args = $"-y -ss {startTime} -to {endTime} -i \"{CurrentFile}\" -c:v libx264 -preset veryfast -b:v {bitrateKbps}k -c:a copy -movflags +faststart \"{outFile}\"";
            var ok = await _ffmpeg.RunFfmpegAsync(args);
            if (!ok && File.Exists(outFile)) File.Delete(outFile);
        }

        // Публичные методы для окна
        public void AddToPlaylist(string path)
        {
            if (!File.Exists(path)) return;
            var ext = Path.GetExtension(path).ToLower();
            if (!IsSupported(ext)) return;
            PlaylistItems.Add(new PlaylistItem { FilePath = path });
        }

        public async Task LoadFile(string path)
        {
            _playback.Stop();
            var info = await _ffprobe.GetInfoAsync(path);
            CurrentFile = path; CurrentFileName = Path.GetFileName(path);
            Resolution = $"{info.Width}x{info.Height}"; FpsStr = $"{info.Fps:0.##}";
            Codec = info.VideoCodec; Bitrate = $"{info.VideoBitrate / 1000:0} kbps";
            Duration = info.Duration > 0 ? TimeSpan.FromSeconds(info.Duration).ToString(@"hh\:mm\:ss") : "??:??:??";
            TotalFrames = (long)(info.Duration * info.Fps);
            AudioInfo = info.HasAudio ? $"{info.AudioCodec} {info.AudioSampleRate / 1000.0:F1}kHz {info.AudioChannels}ch {info.AudioBitrate / 1000:0}kbps" : "none";
            _fileDurationSec = info.Duration; _fileFps = info.Fps; _videoBitrate = info.VideoBitrate; _hasAudio = info.HasAudio;
            _inputMarker = TimeSpan.Zero; _outputMarker = TimeSpan.Zero; _cutMarkers.Clear(); _undoStack.Clear(); SelectedSegment = null;
            RebuildSegments();
            MarkersChanged?.Invoke();
            _playback.Start(path, info.Fps, TimeSpan.Zero, Speed, info.HasAudio);
            IsPlaying = true; StatusText = "▶ Playing";
            OnPropertyChanged(nameof(TotalTimeStr));
        }

        public void UpdatePosition(TimeSpan pos) { CurrentPosition = pos; CurrentFrame = TimeToFrame(pos); OnPropertyChanged(nameof(CurrentTimeStr)); }
        public (double duration, TimeSpan? input, TimeSpan? output, IReadOnlyList<TimeSpan> cuts) GetTimelineData() =>
            (_fileDurationSec, _inputMarker != TimeSpan.Zero ? _inputMarker : null, _outputMarker != TimeSpan.Zero ? _outputMarker : null, _cutMarkers);

        private void RebuildSegments()
        {
            Segments.Clear();
            if (_fileDurationSec <= 0) return;
            var startBound = _inputMarker != TimeSpan.Zero ? _inputMarker : TimeSpan.Zero;
            var endBound = _outputMarker != TimeSpan.Zero ? _outputMarker : TimeSpan.FromSeconds(_fileDurationSec);
            if (endBound <= startBound) return;
            var points = new List<TimeSpan> { startBound };
            points.AddRange(_cutMarkers.Where(c => c > startBound && c < endBound).OrderBy(x => x));
            points.Add(endBound);
            points = points.Distinct().OrderBy(x => x).ToList();
            int idx = 1;
            for (int i = 0; i < points.Count - 1; i++)
            {
                if (points[i + 1] <= points[i]) continue;
                Segments.Add(new SegmentInfo { Index = idx++, Start = points[i], End = points[i + 1], StartFrame = TimeToFrame(points[i]), EndFrame = TimeToFrame(points[i + 1]) });
            }
        }

        private long TimeToFrame(TimeSpan time) => (long)(time.TotalSeconds * _fileFps);
        private static bool IsSupported(string ext) => ext is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" or ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif";
    }

    // Вспомогательные классы
    public class PlaylistItem
    {
        public string FilePath { get; set; } = "";
        public string FileName => Path.GetFileName(FilePath);
        public string Extension => Path.GetExtension(FilePath).ToLower();
        public bool IsVideo => Extension is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm";
        public bool IsImage => Extension is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif";
        public string TypeIcon => IsVideo ? "🎬" : (IsImage ? "🖼️" : "📄");
    }

    public class MontageItem
    {
        public string FilePath { get; set; } = "";
        public string FileName => Path.GetFileName(FilePath);
        public string TypeIcon { get; set; } = "🎬";
        public TimeSpan Duration { get; set; }
        public string DurationStr => Duration.ToString(@"hh\:mm\:ss\.fff");
    }

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