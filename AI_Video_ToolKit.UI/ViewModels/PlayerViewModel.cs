using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using AI_Video_ToolKit.Domain;
using AI_Video_ToolKit.Infrastructure.Services;
using AI_Video_ToolKit.UI.Services;
using AI_Video_ToolKit.UI.Messages;

namespace AI_Video_ToolKit.UI.ViewModels
{
    public partial class PlayerViewModel : ObservableObject, IDisposable
    {
        private readonly PlaybackService _playback;
        private readonly FFprobeService _ffprobe;
        private readonly IMessenger _messenger;
        private readonly Dispatcher _uiDispatcher;

        private MediaInfo? _currentInfo;
        private bool _disposed;

        // Observable properties
        [ObservableProperty] private bool _isPlaying;
        [ObservableProperty] private TimeSpan _currentPosition;
        [ObservableProperty] private double _speed = 1.0;
        [ObservableProperty] private string _currentFileName = "";
        [ObservableProperty] private string _resolution = "";
        [ObservableProperty] private string _fpsStr = "";
        [ObservableProperty] private string _codec = "";
        [ObservableProperty] private string _bitrate = "";
        [ObservableProperty] private string _duration = "";
        [ObservableProperty] private string _audioInfo = "";
        [ObservableProperty] private long _currentFrame;
        [ObservableProperty] private long _totalFrames;

        public double FileFps => _currentInfo?.Fps ?? 25.0;

        public string CurrentFilePath { get; private set; } = "";
        public double DurationSeconds { get; private set; }
        public double Fps { get; private set; }
        public bool HasAudio { get; private set; }
        public long VideoBitrate { get; private set; }
        public TimeSpan TotalDuration => TimeSpan.FromSeconds(DurationSeconds);

        public PlayerViewModel(PlaybackService playback, FFprobeService ffprobe, IMessenger messenger)
        {
            _playback = playback;
            _ffprobe = ffprobe;
            _messenger = messenger;
            _uiDispatcher = Dispatcher.CurrentDispatcher;

            _playback.OnPositionChanged += pos => _uiDispatcher.Invoke(() => CurrentPosition = pos);
            _playback.OnFrameChanged += frame => { }; // можно игнорировать или отправлять сообщение
            _playback.OnPlaybackEnded += () => _uiDispatcher.Invoke(() => IsPlaying = false);

            _messenger.Register<LoadFileMessage>(this, async (r, m) => await LoadFile(m.FilePath));
        }

        private async Task LoadFile(string path)
        {
            try
            {
                _playback.Stop();
                IsPlaying = false;

                // Получаем метаданные
                var info = await _ffprobe.GetInfoAsync(path);
                _currentInfo = info;

                // Обновляем UI-свойства

                DurationSeconds = info.Duration;
                Fps = info.Fps;
                HasAudio = info.HasAudio;
                VideoBitrate = info.VideoBitrate;
                CurrentFilePath = path;

                CurrentFileName = Path.GetFileName(path);
                Resolution = $"{info.Width}x{info.Height}";
                FpsStr = $"{info.Fps:0.##}";
                Codec = info.VideoCodec;
                Bitrate = $"{info.VideoBitrate / 1000:0} kbps";
                Duration = info.Duration > 0 ? TimeSpan.FromSeconds(info.Duration).ToString(@"hh\:mm\:ss") : "??:??:??";
                AudioInfo = info.HasAudio ? $"{info.AudioCodec} {info.AudioSampleRate / 1000.0:F1}kHz {info.AudioChannels}ch {info.AudioBitrate / 1000:0}kbps" : "none";
                TotalFrames = (long)(info.Duration * info.Fps);
                CurrentFrame = 0;
                CurrentPosition = TimeSpan.Zero;

                // Запускаем воспроизведение
                _playback.Start(path, info.Fps, TimeSpan.Zero, Speed, info.HasAudio);
                IsPlaying = true;

                // Оповещаем остальные ViewModel
                _messenger.Send(new FileLoadedMessage(path, info.Duration, info.Fps, info.HasAudio, info.VideoBitrate));
                OnPropertyChanged(nameof(FileFps));
                OnPropertyChanged(nameof(TotalDuration));
            }
            catch (Exception ex)
            {
                _messenger.Send(new LogMessage($"PlayerViewModel error: {ex.Message}"));
            }
        }

        [RelayCommand]
        private void PlayPause()
        {
            if (IsPlaying)
            {
                _playback.Pause();
                IsPlaying = false;
            }
            else
            {
                _playback.Resume();
                IsPlaying = true;
            }
        }

        [RelayCommand]
        private void Stop()
        {
            _playback.Stop();
            IsPlaying = false;
            CurrentPosition = TimeSpan.Zero;
            CurrentFrame = 0;
        }

        [RelayCommand]
        private void Seek(TimeSpan position)
        {
            if (position.TotalSeconds < 0) position = TimeSpan.Zero;
            if (_currentInfo != null && position.TotalSeconds > _currentInfo.Duration)
                position = TimeSpan.FromSeconds(_currentInfo.Duration);
            _playback.SetPosition(position);
            CurrentPosition = position;
        }

        partial void OnSpeedChanged(double value)
        {
            _playback.SetSpeed(value);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _playback.Stop();
            _messenger.Unregister<LoadFileMessage>(this);
        }
    }
}