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
        private double _fps = 25;

        private bool _isPlaying;
        private bool _isPaused;

        private readonly double[] _speeds = { 1, 2, 4, 5, 6, 7, 8, 10, 16 };
        private int _speedIndex = 0;
        private double Speed => _speeds[_speedIndex];

        private int _width;
        private int _height;
        private long _totalFrames;
        private string _codec = "";

        public MainWindow()
        {
            InitializeComponent();

            UpdateSpeedUI();

            _player.OnFrame += f =>
                Dispatcher.Invoke(() => Preview.SetFrame(f));

            _player.OnPositionChanged += t =>
                Dispatcher.Invoke(() =>
                {
                    _current = t;
                    Timeline.SetCurrentTime(t);
                });

            _player.OnPlaybackEnded += () =>
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

            Timeline.OnChanged += Timeline_Changed;
        }

        // ================= LOAD =================

        private async void Load_Click(object? sender, RoutedEventArgs? e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Видео|*.mp4;*.mkv;*.mov;*.avi"
            };

            if (dlg.ShowDialog() != true)
                return;

            await LoadFile(dlg.FileName);
        }

        private async System.Threading.Tasks.Task LoadFile(string path)
        {
            _player.Stop();

            _file = path;

            var info = await _ffprobe.GetInfo(path);

            _duration = info.duration;
            _width = info.width;
            _height = info.height;
            _fps = info.fps > 1 ? info.fps : 25;
            _codec = info.codec;

            _totalFrames = (long)(_duration * _fps);
            _current = TimeSpan.Zero;

            Timeline.SetDuration(_duration);
            Timeline.SetCurrentTime(_current);

            await ShowFrame();

            FileNameText.Text = Path.GetFileName(path);

            UpdateInfoUI();
            SetIdleState();
        }

        // ================= PLAY =================

        private void PlayFrom(TimeSpan time)
        {
            if (_file == null) return;

            _player.Stop();

            _player.Start(_file, 1280, 720, _fps, time, Speed);

            _isPlaying = true;
            _isPaused = false;

            SetPlayState();
        }

        private void TogglePlayPause_Click(object? sender, RoutedEventArgs? e)
        {
            if (_file == null) return;

            if (!_isPlaying && !_isPaused)
            {
                PlayFrom(_current);
                return;
            }

            if (_isPlaying)
            {
                _player.Pause();
                _isPlaying = false;
                _isPaused = true;
                SetPauseState();
                return;
            }

            if (_isPaused)
            {
                _player.Resume();
                _isPlaying = true;
                _isPaused = false;
                SetPlayState();
            }
        }

        // ================= STOP =================

        private void Stop_Click(object? sender, RoutedEventArgs? e)
        {
            _player.Stop();

            _current = TimeSpan.Zero;
            Timeline.SetCurrentTime(_current);

            _ = ShowFrame();

            SetIdleState();
        }

        // ================= SEEK =================

        private async void Timeline_Changed(TimeSpan t)
        {
            _current = t;

            if (_isPlaying)
                PlayFrom(_current);
            else
                await ShowFrame();
        }

        private async System.Threading.Tasks.Task ShowFrame()
        {
            if (_file == null) return;

            var frame = await _grabber.GetFrame(_file, _current, 1280, 720);
            if (frame != null)
                Preview.SetFrame(frame);
        }

        private async void Step(int frames)
        {
            if (_file == null) return;

            _player.Stop();

            _current += TimeSpan.FromSeconds(frames / _fps);

            if (_current < TimeSpan.Zero)
                _current = TimeSpan.Zero;

            if (_current.TotalSeconds > _duration)
                _current = TimeSpan.FromSeconds(_duration);

            Timeline.SetCurrentTime(_current);

            await ShowFrame();

            _isPlaying = false;
            _isPaused = true;
            SetPauseState();
        }

        // ================= SPEED =================

        private void IncreaseSpeed()
        {
            if (_speedIndex < _speeds.Length - 1)
                _speedIndex++;

            UpdateSpeedUI();

            if (_isPlaying)
                PlayFrom(_current);
        }

        private void ResetSpeed()
        {
            _speedIndex = 0;

            UpdateSpeedUI();

            if (_isPlaying)
                PlayFrom(_current);
        }

        private void UpdateSpeedUI()
        {
            SpeedText.Text = $"x{Speed}";
        }

        // ================= UI =================

        private void SetPlayState()
        {
            PlayIcon.Text = "▶";
            PlayIcon.Foreground = System.Windows.Media.Brushes.Green;
        }

        private void SetPauseState()
        {
            PlayIcon.Text = "⏸";
            PlayIcon.Foreground = System.Windows.Media.Brushes.Yellow;
        }

        private void SetIdleState()
        {
            PlayIcon.Text = "▶";
            PlayIcon.Foreground = System.Windows.Media.Brushes.White;

            _isPlaying = false;
            _isPaused = false;
        }

        private void UpdateInfoUI()
        {
            ResolutionText.Text = $"Resolution: {_width}x{_height}";
            FpsText.Text = $"FPS: {_fps:0.##}";
            CodecText.Text = $"Codec: {_codec}";
            DurationText.Text = $"Duration: {TimeSpan.FromSeconds(_duration):hh\\:mm\\:ss} / {_totalFrames} frames";
        }

        // ================= HOTKEY =================

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                TogglePlayPause_Click(null, null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.K)
            {
                _player.Pause();
                _isPlaying = false;
                _isPaused = true;
                SetPauseState();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.S)
            {
                Stop_Click(null, null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Load_Click(null, null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.L)
            {
                IncreaseSpeed();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.J)
            {
                ResetSpeed();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Right)
            {
                Step(Keyboard.Modifiers == ModifierKeys.Shift ? 10 : 1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Left)
            {
                Step(Keyboard.Modifiers == ModifierKeys.Shift ? -10 : -1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.R)
            {
                LoopCheck.IsChecked = !(LoopCheck.IsChecked ?? false);
                e.Handled = true;
            }
        }

        private async void Window_Drop(object? sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);

            if (files.Length == 0)
                return;

            await LoadFile(files[0]);
        }

        private void PlaylistBox_SelectionChanged(object? sender, System.Windows.Controls.SelectionChangedEventArgs e) { }
    }
}