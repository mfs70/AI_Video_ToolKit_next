// Файл: ViewModels/MainViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Win32;
using AI_Video_ToolKit.Infrastructure.Services;
using AI_Video_ToolKit.UI.Services;
using AI_Video_ToolKit.UI.Messages;
using AI_Video_ToolKit.UI.ViewModels;

namespace AI_Video_ToolKit.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        // Сервисы
        private readonly FFprobeService _ffprobe;
        private readonly FFmpegProcessService _ffmpeg;
        private readonly PlaybackService _playback;
        private readonly FrameGrabber _grabber;
        private readonly PlayerViewModel _playerVM;   //
        private readonly PlaylistViewModel _playlistVM;
        public PlaylistViewModel PlaylistVM => _playlistVM;
        private readonly IMessenger _messenger;

        // Состояние

        private string _statusText = "✅ Ready";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }


//        [ObservableProperty] private string _statusText = "✅ Ready";
//        [ObservableProperty] private bool _isPlaying;

        // Метаданные текущего файла
//        [ObservableProperty] private string _currentFile = "";
//        [ObservableProperty] private string _currentFileName = "";
//        [ObservableProperty] private string _resolution = "";
//        [ObservableProperty] private string _fpsStr = "";
//        [ObservableProperty] private string _codec = "";
//        [ObservableProperty] private string _bitrate = "";
//        [ObservableProperty] private string _duration = "";
//        [ObservableProperty] private string _audioInfo = "";

        // Позиция
//        [ObservableProperty] private TimeSpan _currentPosition;
//        [ObservableProperty] private long _currentFrame;
//        [ObservableProperty] private long _totalFrames;
        public string CurrentTimeStr => CurrentPosition.ToString(@"hh\:mm\:ss");
//       public string TotalTimeStr => _fileDurationSec > 0
//            ? TimeSpan.FromSeconds(_fileDurationSec).ToString(@"hh\:mm\:ss")
//            : "00:00:00";

//        private double _fileDurationSec;
//        private double _fileFps = 25;
//        private long _videoBitrate;
//        private bool _hasAudio;

//        public double FileFps => _fileFps;

        // Маркеры и сегменты
        private TimeSpan _inputMarker;
        private TimeSpan _outputMarker;
        private readonly List<TimeSpan> _cutMarkers = new();
        private readonly Stack<(MarkerActionType Type, TimeSpan Value, List<TimeSpan> CutSnapshot)> _undoStack = new();
        public ObservableCollection<SegmentInfo> Segments { get; } = new();

        private SegmentInfo? _selectedSegment;
        public SegmentInfo? SelectedSegment
        {
            get => _selectedSegment;
            set => SetProperty(ref _selectedSegment, value);
        }

        public event Action? MarkersChanged;
        public event Action<BitmapImage>? ImageLoaded;

        // Плейлист и монтажный стол
//        public ObservableCollection<PlaylistItem> PlaylistItems { get; } = new();
        public ObservableCollection<MontageItem> MontageItems { get; } = new();
//        [ObservableProperty] private PlaylistItem? _selectedPlaylistItem;

        // Скорость
        private readonly double[] _speeds = { 0.1, 0.25, 0.5, 1, 2, 4, 8, 16 };
        private int _speedIndex = 3;
//        [ObservableProperty] private int _selectedSpeedIndex = 3;
//        public double Speed => _speeds[_speedIndex];
//        partial void OnSelectedSpeedIndexChanged(int value)
//        {
//            if (value >= 0 && value < _speeds.Length)
//            {
//                _speedIndex = value;
//                OnPropertyChanged(nameof(Speed));
//                _playback.SetSpeed(Speed);
//            }
//        }

        // Конструктор
        public MainViewModel(FFprobeService ffprobe, FFmpegProcessService ffmpeg,
            PlaybackService playback, FrameGrabber grabber, PlaylistViewModel playlistVM, IMessenger messenger, PlayerViewModel playerVM)
        {
            _ffprobe = ffprobe; _ffmpeg = ffmpeg; _playback = playback; _grabber = grabber; _playlistVM = playlistVM;
            _playerVM = playerVM;
            _playlistVM.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PlaylistViewModel.SelectedItem))
                {
                    OnPropertyChanged(nameof(SelectedPlaylistItem));
                }
            };
            _playback.OnFrameChanged += _ => { };
            _playback.OnPositionChanged += pos =>
            {
                RunOnUiThread(() => UpdatePosition(pos));
            };
            _playback.OnPlaybackEnded += () =>
            {
                RunOnUiThread(() =>
                {
                    IsPlaying = false;
                    StatusText = "⏸ Paused";
                });
            };
            // Подписка на сообщение LoadFileMessage в конструкторе MainViewModel
            WeakReferenceMessenger.Default.Register<LoadFileMessage>(this, async (r, m) =>
            {
                await LoadFile(m.FilePath);
            });

            _messenger = messenger;
            _messenger.Register<LoadFileMessage>(this, async (r, m) => await LoadFile(m.FilePath));

        }


        // Прокси-свойства (замените существующие соответствующие свойства)
        public bool IsPlaying { get => _playerVM.IsPlaying; set => _playerVM.IsPlaying = value; }
        public TimeSpan CurrentPosition { get => _playerVM.CurrentPosition; set => _playerVM.CurrentPosition = value; }
        public double Speed => _playerVM.Speed;
        public int SelectedSpeedIndex
        {
            get => _speedIndex; // оставляем старую логику скорости? лучше перенести в PlayerViewModel, но пока оставим как есть
            set { _speedIndex = value; OnPropertyChanged(); _playerVM.Speed = _speeds[_speedIndex]; }
        }
        public string CurrentFileName { get => _playerVM.CurrentFileName; set => _playerVM.CurrentFileName = value; }
        public string Resolution { get => _playerVM.Resolution; set => _playerVM.Resolution = value; }
        public string FpsStr { get => _playerVM.FpsStr; set => _playerVM.FpsStr = value; }
        public string Codec { get => _playerVM.Codec; set => _playerVM.Codec = value; }
        public string Bitrate { get => _playerVM.Bitrate; set => _playerVM.Bitrate = value; }
        public string Duration { get => _playerVM.Duration; set => _playerVM.Duration = value; }
        public string AudioInfo { get => _playerVM.AudioInfo; set => _playerVM.AudioInfo = value; }
        public long CurrentFrame { get => _playerVM.CurrentFrame; set => _playerVM.CurrentFrame = value; }
        public long TotalFrames { get => _playerVM.TotalFrames; set => _playerVM.TotalFrames = value; }
//        public double FileFps => _playerVM.FileFps;
        public string TotalTimeStr => _playerVM.TotalDuration.ToString(@"hh\:mm\:ss");
        public string CurrentFile => _playerVM.CurrentFilePath;

        public double FileDurationSec => _playerVM.DurationSeconds;
        public double FileFps => _playerVM.Fps;
        public bool HasAudio => _playerVM.HasAudio;
        public long VideoBitrate => _playerVM.VideoBitrate;


        // Прокси для совместимости со старым кодом
        public ObservableCollection<PlaylistItem> PlaylistItems => _playlistVM.Items;
        public PlaylistItem? SelectedPlaylistItem
        {
            get => _playlistVM.SelectedItem;
            set => _playlistVM.SelectedItem = value;
        }
        public ICommand ClearPlaylistCommand => _playlistVM.ClearCommand;
        public ICommand RemoveSelectedFromPlaylistCommand => _playlistVM.RemoveSelectedCommand;
        // прокси для команд MoveNext/MovePrevious
        public ICommand NextCommand => _playlistVM.MoveNextCommand;
        public ICommand PreviousCommand => _playlistVM.MovePreviousCommand;
        public ICommand AddFilesCommand => _playlistVM.AddFilesCommand;
        // Прокси-команды
        public ICommand PlayPauseCommand => _playerVM.PlayPauseCommand;
        public ICommand StopCommand => _playerVM.StopCommand;
        public ICommand SeekCommand => _playerVM.SeekCommand;
        // Удалите старые методы PlayPause, Stop, LoadFile и т.д. (перенесены в PlayerViewModel)
        // Но LoadFile оставьте как вызов _playerVM.LoadFile (через сообщение?)


        // Для совместимости со старыми методами (если они вызываются из кода)
        public void  Next() => NextCommand.Execute(null);
        public void  Previous() => PreviousCommand.Execute(null);

        public bool AddToPlaylist(string path) => _playlistVM.AddToPlaylist(path);

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

//        [RelayCommand] private async Task ClearPlaylist() { PlaylistItems.Clear(); Segments.Clear(); MarkersChanged?.Invoke(); await Task.CompletedTask; }
//        [RelayCommand]
//        private async Task RemoveSelectedFromPlaylist()
//        {
//            if (SelectedPlaylistItem == null) return;
//
//            var removedIndex = PlaylistItems.IndexOf(SelectedPlaylistItem);
//            PlaylistItems.Remove(SelectedPlaylistItem);
//
//            // Keep keyboard/Next navigation anchored after deletion by selecting the
//            // item that slid into the removed row, or the previous item at the end.
//           if (PlaylistItems.Count > 0)
//                SelectedPlaylistItem = PlaylistItems[Math.Min(removedIndex, PlaylistItems.Count - 1)];
//
//            await Task.CompletedTask;
//        }

//        [RelayCommand]
//        private async Task PlayPause()
//        {
//            if (string.IsNullOrEmpty(CurrentFile))
//            {
//                if (PlaylistItems.Count > 0) await LoadFile(PlaylistItems[0].FilePath);
//                return;
//            }
//            if (_playback.IsPlaying) { _playback.Pause(); IsPlaying = false; StatusText = "⏸ Paused"; }
//            else { _playback.Resume(); IsPlaying = true; StatusText = "▶ Playing"; }
//            await Task.CompletedTask;
//        }

//        [RelayCommand]
//        private async Task Stop()
//        {
//            _playback.Stop(); IsPlaying = false; StatusText = "⏹ Stopped";
//            CurrentPosition = TimeSpan.Zero; CurrentFrame = 0;
//            OnPropertyChanged(nameof(CurrentTimeStr));
//            await Task.CompletedTask;
//        }

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
            _playback.Start(CurrentFile, FileFps, SelectedSegment.Start, Speed, HasAudio, SelectedSegment.End);
//заменил   _playback.Start(CurrentFile, _fileFps, SelectedSegment.Start, Speed, _hasAudio);
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
            var bitrateKbps = Math.Max(1500, (int)((_playerVM.VideoBitrate > 0 ? _playerVM.VideoBitrate : 4_000_000) / 1000));
            var args = $"-y -ss {startTime} -to {endTime} -i \"{CurrentFile}\" -c:v libx264 -preset veryfast -b:v {bitrateKbps}k -c:a aac -ar 48000 -vsync cfr -async 1 -reset_timestamps 1 -movflags +faststart \"{outFile}\"";
			// var args = $"-y -ss {startTime} -to {endTime} -i \"{CurrentFile}\" -c:v libx264 -preset veryfast -b:v {bitrateKbps}k -c:a copy -movflags +faststart \"{outFile}\"";
            
			var ok = await _ffmpeg.RunFfmpegAsync(args);
            if (!ok && File.Exists(outFile)) File.Delete(outFile);
        }

        // Публичные методы для окна
//        public bool AddToPlaylist(string path)
//        {
//            if (!File.Exists(path)) return false;
//            var ext = Path.GetExtension(path).ToLower();
//            if (!IsSupported(ext)) return false;
//
//            if (PlaylistItems.Any(item => string.Equals(item.FilePath, path, StringComparison.OrdinalIgnoreCase)))
//                return false;
//
//            PlaylistItems.Add(new PlaylistItem { FilePath = path });
//            return true;
//        }

        public async Task LoadFile(string path)
        {
            _playback.Stop();
 //           SelectedPlaylistItem = PlaylistItems.FirstOrDefault(item => item.FilePath == path);
            _playlistVM.SelectedItem = _playlistVM.Items.FirstOrDefault(item => item.FilePath == path);
            if (IsImage(path))
            {
                _playback.ClearMedia();
                LoadImageFile(path);
                return;
            }

            var info = await _ffprobe.GetInfoAsync(path);
//            CurrentFile = path; 
            CurrentFileName = Path.GetFileName(path);
            Resolution = $"{info.Width}x{info.Height}"; FpsStr = $"{info.Fps:0.##}";
            Codec = info.VideoCodec; Bitrate = $"{info.VideoBitrate / 1000:0} kbps";
            Duration = info.Duration > 0 ? TimeSpan.FromSeconds(info.Duration).ToString(@"hh\:mm\:ss") : "??:??:??";
            TotalFrames = (long)(info.Duration * info.Fps);
            AudioInfo = info.HasAudio ? $"{info.AudioCodec} {info.AudioSampleRate / 1000.0:F1}kHz {info.AudioChannels}ch {info.AudioBitrate / 1000:0}kbps" : "none";
 //           _playerVM.DurationSeconds = info.Duration;
 //           _playerVM.Fps = info.Fps; _playerVM.VideoBitrate = info.VideoBitrate; _playerVM.HasAudio = info.HasAudio;
            OnPropertyChanged(nameof(FileFps));
            _inputMarker = TimeSpan.Zero; _outputMarker = TimeSpan.Zero; _cutMarkers.Clear(); _undoStack.Clear(); SelectedSegment = null;
            RebuildSegments();
            MarkersChanged?.Invoke();
            UpdatePosition(TimeSpan.Zero);
            _playback.Start(path, info.Fps, TimeSpan.Zero, Speed, info.HasAudio);
            IsPlaying = true; StatusText = "▶ Playing";
            OnPropertyChanged(nameof(TotalTimeStr));
        }

        public void UpdatePosition(TimeSpan pos) { CurrentPosition = pos; CurrentFrame = TimeToFrame(pos); OnPropertyChanged(nameof(CurrentTimeStr)); }
        public (double duration, TimeSpan? input, TimeSpan? output, IReadOnlyList<TimeSpan> cuts) GetTimelineData() =>
            (_playerVM.DurationSeconds, _inputMarker != TimeSpan.Zero ? _inputMarker : null, _outputMarker != TimeSpan.Zero ? _outputMarker : null, _cutMarkers);

        public void MoveTimelineMarker(string markerType, TimeSpan? original, TimeSpan moved)
        {
            moved = ClampToMedia(SnapToFrame(moved));
            var frame = TimeSpan.FromSeconds(1 / Math.Max(0.0001, _playerVM.Fps));

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
                var max = _outputMarker != TimeSpan.Zero ? _outputMarker - frame : TimeSpan.FromSeconds(_playerVM.DurationSeconds) - frame;
                moved = TimeSpan.FromSeconds(Math.Clamp(moved.TotalSeconds, min.TotalSeconds, max.TotalSeconds));
                if (_cutMarkers.Where((_, i) => i != index).Any(c => (c - moved).Duration() < frame))
                    return;

                _cutMarkers[index] = moved;
                _cutMarkers.Sort();
            }

            RebuildSegments();
            MarkersChanged?.Invoke();
        }

        private static void RunOnUiThread(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            // Playback callbacks are produced by worker tasks. Queue property changes
            // asynchronously so decoding never blocks on the UI thread.
            dispatcher.BeginInvoke(action);
        }

        private void RebuildSegments()
        {
            Segments.Clear();
            if (_playerVM.DurationSeconds <= 0) return;
            var startBound = _inputMarker != TimeSpan.Zero ? _inputMarker : TimeSpan.Zero;
            var endBound = _outputMarker != TimeSpan.Zero ? _outputMarker : TimeSpan.FromSeconds(_playerVM.DurationSeconds);
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

        private long TimeToFrame(TimeSpan time) => (long)(time.TotalSeconds * _playerVM.Fps);
        private TimeSpan SnapToFrame(TimeSpan time) => TimeSpan.FromSeconds(Math.Round(time.TotalSeconds * _playerVM.Fps) / _playerVM.Fps);
        private TimeSpan ClampToMedia(TimeSpan time) => TimeSpan.FromSeconds(Math.Clamp(time.TotalSeconds, 0, Math.Max(0, _playerVM.DurationSeconds)));
        private static bool IsSupported(string ext) => ext is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" or ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif";
        private static bool IsImage(string path) => Path.GetExtension(path).ToLower() is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif";

        private void LoadImageFile(string path)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

 //           CurrentFile = path;
            CurrentFileName = Path.GetFileName(path);
            Resolution = $"{bitmap.PixelWidth}x{bitmap.PixelHeight}";
            FpsStr = "-";
            Codec = "image";
            Bitrate = "-";
            Duration = "00:00:00";
            AudioInfo = "none";
            CurrentPosition = TimeSpan.Zero;
            CurrentFrame = 0;
            TotalFrames = 1;
 //           _playerVM.DurationSeconds = 0;
 //           _playerVM.Fps = 1;
            OnPropertyChanged(nameof(FileFps));
 //           _playerVM.VideoBitrate = 0;
 //           _playerVM.HasAudio = false;
            _inputMarker = TimeSpan.Zero;
            _outputMarker = TimeSpan.Zero;
            _cutMarkers.Clear();
            _undoStack.Clear();
            Segments.Clear();
            IsPlaying = false;
            StatusText = "🖼 Image loaded";
            OnPropertyChanged(nameof(CurrentTimeStr));
            OnPropertyChanged(nameof(TotalTimeStr));
            MarkersChanged?.Invoke();
            ImageLoaded?.Invoke(bitmap);
        }
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
