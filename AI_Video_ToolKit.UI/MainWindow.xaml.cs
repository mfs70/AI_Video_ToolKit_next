using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using AI_Video_ToolKit.UI.Services;

namespace AI_Video_ToolKit.UI
{
    public partial class MainWindow : Window
    {
        private readonly BufferedVideoPlayer _player = new();
        private readonly FFprobeService _ffprobe = new();
        private readonly FrameGrabber _grabber = new();

        private string? _currentFile;

        private double _fps;
        private TimeSpan _currentTime = TimeSpan.Zero;

        private bool _isLoaded = false;
        private bool _isPlaying = false;
        private bool _isSeeking = false;

        public MainWindow()
        {
            InitializeComponent();

            Loaded += (_, __) => Keyboard.Focus(this);

            _player.OnFrame += frame =>
                Dispatcher.Invoke(() => Preview.SetFrame(frame));

            _player.OnPositionChanged += time =>
            {
                _currentTime = time;

                if (_isSeeking) return;

                Dispatcher.Invoke(() =>
                    Timeline.SetCurrentTime(time));
            };

            _player.OnPlaybackEnded += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (LoopCheck.IsChecked == true)
                    {
                        RestartPlayback(TimeSpan.Zero);
                    }
                    else
                    {
                        _isPlaying = false;
                    }
                });
            };

            Timeline.OnUserInteraction += b => _isSeeking = b;

            Timeline.OnTimeChanged += time =>
            {
                if (!_isLoaded) return;

                _currentTime = time;

                if (!_isSeeking)
                {
                    RestartPlayback(_currentTime);
                }
            };
        }

        // ================= LOAD =================

        private async void Load_Click(object? sender, RoutedEventArgs? e)
        {
            var dlg = new OpenFileDialog();

            if (dlg.ShowDialog() != true)
                return;

            try
            {
                _player.Stop();

                _currentFile = dlg.FileName;

                var info = await _ffprobe.GetInfo(_currentFile);

                _fps = info.fps;
                _currentTime = TimeSpan.Zero;

                Timeline.SetDuration(info.duration);
                Timeline.SetCurrentTime(TimeSpan.Zero);

                var frame = await _grabber.GetFrame(_currentFile, _currentTime, 1280, 720);
                Preview.SetFrame(frame);

                FileNameText.Text = Path.GetFileName(_currentFile);

                _isLoaded = true;
                _isPlaying = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        // ================= PLAY =================

        private void StartPlayback(TimeSpan start)
        {
            if (!_isLoaded || _currentFile == null) return;

            _player.Start(_currentFile, 1280, 720, _fps, start);

            _isPlaying = true;
        }

        private void RestartPlayback(TimeSpan start)
        {
            _player.Stop();
            _player.Start(_currentFile!, 1280, 720, _fps, start);
            _isPlaying = true;
        }

        private void TogglePlayPause_Click(object? sender, RoutedEventArgs? e)
        {
            if (!_isLoaded) return;

            if (!_isPlaying)
            {
                StartPlayback(_currentTime);
            }
            else
            {
                _player.Pause();
                _isPlaying = false;
            }
        }

        private void Stop_Click(object? sender, RoutedEventArgs? e)
        {
            _player.Stop();

            _isPlaying = false;
            _isLoaded = false;
            _currentFile = null;

            _currentTime = TimeSpan.Zero;

            Preview.SetFrame(null);
            Timeline.SetCurrentTime(TimeSpan.Zero);
            FileNameText.Text = "";
        }

        // ================= HOTKEY =================

        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.L)
            {
                Load_Click(null, null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Space)
            {
                TogglePlayPause_Click(null, null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.S)
            {
                Stop_Click(null, null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.R)
            {
                LoopCheck.IsChecked = !(LoopCheck.IsChecked ?? false);
                e.Handled = true;
                return;
            }
        }

        // ================= DRAG =================

        private async void Window_Drop(object? sender, DragEventArgs? e)
        {
            if (e == null) return;

            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                _currentFile = files[0];

                var info = await _ffprobe.GetInfo(_currentFile);

                _fps = info.fps;
                _currentTime = TimeSpan.Zero;

                Timeline.SetDuration(info.duration);
                Timeline.SetCurrentTime(TimeSpan.Zero);

                var frame = await _grabber.GetFrame(_currentFile, _currentTime, 1280, 720);
                Preview.SetFrame(frame);

                FileNameText.Text = Path.GetFileName(_currentFile);

                _isLoaded = true;
                _isPlaying = false;
            }
        }

        private void PlaylistBox_SelectionChanged(object? sender, System.Windows.Controls.SelectionChangedEventArgs? e)
        {
        }
    }
}