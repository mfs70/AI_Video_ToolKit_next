// Файл: Services/PlaybackService.cs (проект AI_Video_ToolKit.UI)
using System;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;   // BitmapSource

namespace AI_Video_ToolKit.UI.Services
{
    public class PlaybackService
    {
        private readonly BufferedVideoPlayer _player;
        private readonly FrameGrabber _grabber;

        public event Action<BitmapSource>? OnFrameChanged;  // теперь BitmapSource
        public event Action<TimeSpan>? OnPositionChanged;
        public event Action? OnPlaybackEnded;

        public bool IsPlaying => _isPlaying;
        public TimeSpan CurrentPosition => _current;

        private string? _currentFile;
        private TimeSpan _current;
        private double _fps = 25;
        private double _speed = 1.0;
        private bool _isPlaying;
        private bool _audioEnabled;

        public PlaybackService(BufferedVideoPlayer player, FrameGrabber grabber)
        {
            _player = player;
            _grabber = grabber;

            _player.OnFrame += frame => OnFrameChanged?.Invoke(frame);
            _player.OnPositionChanged += pos =>
            {
                _current = pos;
                OnPositionChanged?.Invoke(pos);
            };
            _player.OnPlaybackEnded += () =>
            {
                _isPlaying = false;
                OnPlaybackEnded?.Invoke();
            };
        }

        public void Start(string file, double fps, TimeSpan startPosition,
                          double speed = 1.0, bool enableAudio = true)
        {
            Stop();
            _currentFile = file;
            _fps = fps;
            _speed = speed;
            _audioEnabled = enableAudio;
            _current = startPosition;
            _player.Start(file, 1280, 720, fps, startPosition, speed, enableAudio);
            _isPlaying = true;
        }

        public void Pause()
        {
            _player.Pause();
            _isPlaying = false;
        }

        public void Resume()
        {
            _player.Resume();
            _isPlaying = true;
        }

        public void Stop()
        {
            _player.Stop();
            _isPlaying = false;
        }

        public void SetSpeed(double speed)
        {
            _speed = speed;
            if (_isPlaying && _currentFile != null)
            {
                _player.Stop();
                _player.Start(_currentFile, 1280, 720, _fps, _current, speed, _audioEnabled);
            }
        }

        public async Task<BitmapSource?> GrabCurrentFrame()
        {
            if (_currentFile == null) return null;
            return await _grabber.GetFrame(_currentFile, _current, 1280, 720);
        }
    }
}