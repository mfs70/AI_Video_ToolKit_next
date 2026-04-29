using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using AI_Video_ToolKit.UI.Services;

namespace AI_Video_ToolKit.UI
{
    public partial class MainWindow : Window
    {
        private string? _file;

        private readonly BufferedVideoPlayer _player = new();
        private readonly FFprobeService _ffprobe = new();
        private readonly FrameGrabber _grabber = new();

        private double _fps;
        private TimeSpan _currentTime = TimeSpan.Zero;

        private CancellationTokenSource? _seekCts;

        private bool _userInteracting = false;
        private bool _isPlaying = false;
        private bool _hasActiveSession = false;

        // 🔥 КЛЮЧ
        private bool _seekChangedWhilePaused = false;

        public MainWindow()
        {
            InitializeComponent();

            _player.OnFrame += frame =>
            {
                Dispatcher.Invoke(() => Preview.SetFrame(frame));
            };

            _player.OnPositionChanged += time =>
            {
                _currentTime = time;

                if (_userInteracting) return;

                Dispatcher.Invoke(() =>
                {
                    Timeline.SetCurrentTime(time);
                });
            };

            _player.OnPlaybackEnded += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (LoopCheck.IsChecked == true)
                    {
                        _currentTime = TimeSpan.Zero;
                        RestartPlayback();
                    }
                    else
                    {
                        _isPlaying = false;
                        _hasActiveSession = false;
                    }
                });
            };

            Timeline.OnUserInteraction += isActive =>
            {
                _userInteracting = isActive;
            };

            Timeline.OnTimeChanged += Timeline_OnTimeChanged;
        }

        private async void Load_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();

            if (dlg.ShowDialog() != true)
                return;

            _player.Stop();

            _file = dlg.FileName;

            var info = await _ffprobe.GetInfo(_file);

            _fps = info.fps;

            _currentTime = TimeSpan.Zero;
            _isPlaying = true;
            _hasActiveSession = false;
            _seekChangedWhilePaused = false;

            Timeline.SetDuration(info.duration);
            Timeline.SetCurrentTime(TimeSpan.Zero);

            Play();
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if (_file == null) return;

            // 🔥 ЕСЛИ БЫЛ SEEK В ПАУЗЕ → СТАРТ С НОВОЙ ПОЗИЦИИ
            if (_seekChangedWhilePaused)
            {
                _seekChangedWhilePaused = false;
                Play();
                return;
            }

            // 🔥 ОБЫЧНЫЙ RESUME
            if (_hasActiveSession && !_isPlaying)
            {
                _isPlaying = true;
                _player.Resume();
                return;
            }

            Play();
        }

        private void Play()
        {
            if (_file == null) return;

            _isPlaying = true;
            _hasActiveSession = true;

            _player.Start(_file, 1280, 720, _fps, _currentTime);
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            _isPlaying = false;
            _player.Pause();
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            _isPlaying = false;
            _hasActiveSession = false;

            _player.Stop();
            _currentTime = TimeSpan.Zero;

            Timeline.SetCurrentTime(TimeSpan.Zero);
        }

        private void Prev_Click(object sender, RoutedEventArgs e)
        {
            _currentTime = TimeSpan.Zero;
            RestartPlayback();
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            _currentTime = TimeSpan.Zero;
            RestartPlayback();
        }

        private void RestartPlayback()
        {
            if (_file == null) return;

            _player.Stop();
            _player.Start(_file, 1280, 720, _fps, _currentTime);

            _hasActiveSession = true;

            if (!_isPlaying)
            {
                _player.Pause();
            }
        }

        private void Timeline_OnTimeChanged(TimeSpan time)
        {
            if (_file == null) return;

            _currentTime = time;

            // 🔥 фикс
            if (!_isPlaying)
            {
                _seekChangedWhilePaused = true;
            }

            _seekCts?.Cancel();
            _seekCts = new CancellationTokenSource();

            var token = _seekCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(120, token);

                    if (token.IsCancellationRequested) return;

                    if (_isPlaying)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            RestartPlayback();
                        });
                    }
                    else
                    {
                        var frame = await _grabber.GetFrame(_file, _currentTime, 1280, 720);

                        if (frame != null)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                Preview.SetFrame(frame);
                                Timeline.SetCurrentTime(_currentTime);
                            });
                        }
                    }
                }
                catch { }
            });
        }
    }
}