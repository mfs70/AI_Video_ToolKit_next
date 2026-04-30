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

        private string? _file;
        private double _duration;
        private TimeSpan _current = TimeSpan.Zero;

        private bool _isPlaying;
        private bool _isPaused;

        public MainWindow()
        {
            InitializeComponent();

            _player.OnFrame += f =>
                Dispatcher.Invoke(() => Preview.SetFrame(f));

            _player.OnPositionChanged += t =>
            {
                Dispatcher.Invoke(() =>
                {
                    _current = t;
                    Timeline.SetCurrentTime(t);
                });
            };

            _player.OnPlaybackEnded += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (LoopCheck.IsChecked == true)
                    {
                        PlayFrom(TimeSpan.Zero);
                        return;
                    }

                    _current = TimeSpan.Zero;
                    Timeline.SetCurrentTime(_current);

                    SetIdleState();
                });
            };

            Timeline.OnChanged += Timeline_Changed;

            Loaded += (_, __) => Keyboard.Focus(this);
        }

        // ================= LOAD =================

        private async void Load_Click(object? sender, RoutedEventArgs? e)
        {
            var dlg = new OpenFileDialog();
            if (dlg.ShowDialog() != true) return;

            _player.Stop();

            _file = dlg.FileName;

            var info = await _ffprobe.GetInfo(_file);

            _duration = info.duration;
            _current = TimeSpan.Zero;

            Timeline.SetDuration(_duration);
            Timeline.SetCurrentTime(_current);

            var frame = await _grabber.GetFrame(_file, _current, 1280, 720);
            Preview.SetFrame(frame);

            FileNameText.Text = Path.GetFileName(_file);

            SetIdleState();
        }

        // ================= PLAY =================

        private void PlayFrom(TimeSpan time)
        {
            if (_file == null) return;

            _player.Stop();

            _current = time;

            _player.Start(_file, 1280, 720, 0, _current);

            _isPlaying = true;
            _isPaused = false;

            SetPlayState();
        }

        private void TogglePlayPause_Click(object? sender, RoutedEventArgs? e)
        {
            if (_file == null) return;

            // старт
            if (!_isPlaying && !_isPaused)
            {
                PlayFrom(_current);
                return;
            }

            // пауза
            if (_isPlaying)
            {
                _player.Pause();
                _isPlaying = false;
                _isPaused = true;

                SetPauseState();
                return;
            }

            // resume
            if (_isPaused)
            {
                _player.Resume();
                _isPaused = false;
                _isPlaying = true;

                SetPlayState();
            }
        }

        private void Stop_Click(object? sender, RoutedEventArgs? e)
        {
            _player.Stop();

            _current = TimeSpan.Zero;

            Timeline.SetCurrentTime(_current);
            Preview.SetFrame(null);
            FileNameText.Text = "";

            SetIdleState();
        }

        // ================= SEEK =================

        private async void Timeline_Changed(TimeSpan time)
        {
            _current = time;

            if (_isPlaying)
            {
                PlayFrom(_current);
            }
            else if (_file != null)
            {
                var frame = await _grabber.GetFrame(_file, _current, 1280, 720);
                Preview.SetFrame(frame);
            }
        }

        // ================= UI =================

        private void SetPlayState()
        {
            PlayButton.Content = "Play";
            PlayButton.Foreground = System.Windows.Media.Brushes.Green;
        }

        private void SetPauseState()
        {
            PlayButton.Content = "Pause";
            PlayButton.Foreground = System.Windows.Media.Brushes.Yellow;
        }

        private void SetIdleState()
        {
            _isPlaying = false;
            _isPaused = false;

            PlayButton.Content = "Play";
            PlayButton.Foreground = System.Windows.Media.Brushes.White;
        }

        // ================= HOTKEY =================

        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
                TogglePlayPause_Click(null, null);

            if (e.Key == Key.L)
                Load_Click(null, null);

            if (e.Key == Key.S)
                Stop_Click(null, null);

            if (e.Key == Key.R)
                LoopCheck.IsChecked = !(LoopCheck.IsChecked ?? false);
        }

        private void Window_Drop(object? sender, DragEventArgs e) { }
        private void PlaylistBox_SelectionChanged(object? sender, System.Windows.Controls.SelectionChangedEventArgs e) { }
    }
}