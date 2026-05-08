// Файл: ViewModels/MainViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using AI_Video_ToolKit.Infrastructure.Services;
using AI_Video_ToolKit.UI.Services;

namespace AI_Video_ToolKit.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly FFprobeService _ffprobe;
        private readonly FFmpegProcessService _ffmpeg;
        private readonly PlaybackService _playback;
        private readonly FrameGrabber _grabber;

        // ==================== Состояние ====================
        [ObservableProperty] private string _statusText = "✅ Ready";
        [ObservableProperty] private bool _isPlaying;

        // ==================== Метаданные текущего файла ====================
        [ObservableProperty] private string _currentFile = "";
        [ObservableProperty] private string _currentFileName = "";
        [ObservableProperty] private string _resolution = "";
        [ObservableProperty] private string _fps = "";
        [ObservableProperty] private string _codec = "";
        [ObservableProperty] private string _bitrate = "";
        [ObservableProperty] private string _duration = "";
        [ObservableProperty] private string _audioInfo = "";

        // ==================== Таймлайн и позиция ====================
        [ObservableProperty] private TimeSpan _currentPosition;
        [ObservableProperty] private long _currentFrame;
        [ObservableProperty] private long _totalFrames;

        // ==================== Плейлист и монтажный стол ====================
        public ObservableCollection<PlaylistItem> PlaylistItems { get; } = new();
        public ObservableCollection<MontageItem> MontageItems { get; } = new();

        [ObservableProperty] private PlaylistItem? _selectedPlaylistItem;

        // ==================== Скорость ====================
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

        // ==================== Конструктор ====================
        public MainViewModel(FFprobeService ffprobe,
                             FFmpegProcessService ffmpeg,
                             PlaybackService playback,
                             FrameGrabber grabber)
        {
            _ffprobe = ffprobe;
            _ffmpeg = ffmpeg;
            _playback = playback;
            _grabber = grabber;

            _playback.OnFrameChanged += frame =>
            {
                // кадр передаётся напрямую в VideoPreviewControl через MainWindow
            };
            _playback.OnPositionChanged += pos =>
            {
                CurrentPosition = pos;
                CurrentFrame = TimeToFrame(pos);
            };
            _playback.OnPlaybackEnded += () =>
            {
                IsPlaying = false;
                StatusText = "⏸ Paused";
            };
        }

        // ==================== Команды ====================
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
                    foreach (var path in dlg.FileNames)
                        AddToPlaylist(path);
                });
                if (PlaylistItems.Count > 0 && string.IsNullOrEmpty(CurrentFile))
                    await LoadFile(PlaylistItems[0].FilePath);
            }
        }

        [RelayCommand]
        private async Task ClearPlaylist()
        {
            PlaylistItems.Clear();
            StatusText = "Playlist cleared";
            await Task.CompletedTask;
        }

        [RelayCommand]
        private async Task RemoveSelectedFromPlaylist()
        {
            if (SelectedPlaylistItem != null)
            {
                PlaylistItems.Remove(SelectedPlaylistItem);
                SelectedPlaylistItem = null;
            }
            await Task.CompletedTask;
        }

        [RelayCommand]
        private async Task PlayPause()
        {
            if (string.IsNullOrEmpty(CurrentFile))
            {
                if (PlaylistItems.Count > 0)
                    await LoadAndPlay(PlaylistItems[0].FilePath);
                return;
            }

            if (_playback.IsPlaying)
            {
                _playback.Pause();
                IsPlaying = false;
                StatusText = "⏸ Paused";
            }
            else
            {
                _playback.Resume();
                IsPlaying = true;
                StatusText = "▶ Playing";
            }
            await Task.CompletedTask;
        }

        [RelayCommand]
        private async Task Stop()
        {
            _playback.Stop();
            IsPlaying = false;
            StatusText = "⏹ Stopped";
            await Task.CompletedTask;
        }

        [RelayCommand]
        private async Task Next()
        {
            if (PlaylistItems.Count == 0) return;
            int idx = PlaylistItems.IndexOf(SelectedPlaylistItem!);
            if (idx < 0) idx = 0;
            idx = (idx + 1) % PlaylistItems.Count;
            await LoadAndPlay(PlaylistItems[idx].FilePath);
        }

        [RelayCommand]
        private async Task Previous()
        {
            if (PlaylistItems.Count == 0) return;
            int idx = PlaylistItems.IndexOf(SelectedPlaylistItem!);
            if (idx < 0) idx = 0;
            idx = (idx - 1 + PlaylistItems.Count) % PlaylistItems.Count;
            await LoadAndPlay(PlaylistItems[idx].FilePath);
        }

        // ==================== Открытые методы для Drag&Drop (вызывает MainWindow) ====================
        public void AddToPlaylist(string path)
        {
            if (!File.Exists(path)) return;
            var ext = Path.GetExtension(path).ToLower();
            if (!IsSupported(ext)) return;

            PlaylistItems.Add(new PlaylistItem
            {
                FilePath = path
            });
        }

        public async Task LoadFile(string path)
        {
            _playback.Stop();
            var info = await _ffprobe.GetInfoAsync(path);

            CurrentFile = path;
            CurrentFileName = Path.GetFileName(path);
            Resolution = $"{info.Width}x{info.Height}";
            Fps = $"{info.Fps:0.##}";
            Codec = info.VideoCodec;
            Bitrate = $"{info.VideoBitrate / 1000:0} kbps";
            Duration = info.Duration > 0 ? TimeSpan.FromSeconds(info.Duration).ToString(@"hh\:mm\:ss") : "??:??:??";
            TotalFrames = (long)(info.Duration * info.Fps);
            AudioInfo = info.HasAudio
                ? $"{info.AudioCodec} {info.AudioSampleRate / 1000.0:F1}kHz {info.AudioChannels}ch {info.AudioBitrate / 1000:0}kbps"
                : "none";

            _playback.Start(path, info.Fps, TimeSpan.Zero, Speed, AudioInfo != "none");
            IsPlaying = true;
            StatusText = "▶ Playing";
        }

        // ==================== Утилиты ====================
        private async Task LoadAndPlay(string path)
        {
            await LoadFile(path);
        }

        private long TimeToFrame(TimeSpan time) => (long)(time.TotalSeconds * (double.TryParse(Fps, out var f) ? f : 25));

        private static bool IsSupported(string ext) =>
            ext is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm"
                or ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif";
    }

    // ==================== Вспомогательные модели для плейлиста ====================
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
}