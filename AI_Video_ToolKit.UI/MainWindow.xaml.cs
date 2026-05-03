using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
        private double _fps = 25;

        private TimeSpan _current = TimeSpan.Zero;
        private long _currentFrame;

        private bool _isPlaying;
        private bool _isPaused;
        private bool _isHandlingPlaybackEnd;

        private readonly double[] _speeds = { 1, 2, 4, 8, 16 };
        private int _speedIndex;
        private double Speed => _speeds[_speedIndex];

        private int _width;
        private int _height;
        private long _totalFrames;
        private string _codec = "";

        public MainWindow()
        {
            InitializeComponent();
            UpdateSpeedUI();

            _player.OnFrame += f => Dispatcher.Invoke(() => Preview.SetFrame(f));
            _player.OnPositionChanged += pos => Dispatcher.Invoke(() =>
            {
                _current = ClampToDuration(pos);
                _currentFrame = TimeToFrame(_current);
                Timeline.SetCurrentTime(_current);
                Timeline.SetFrameInfo(_currentFrame, _totalFrames);
            });
            _player.OnPlaybackEnded += () => Dispatcher.Invoke(HandlePlaybackEnd);

            Timeline.OnChanged += Timeline_Changed;

            Log("MainWindow initialized.");
        }

        private void Log(string text)
        {
            LogList.Items.Add($"[{DateTime.Now:HH:mm:ss}] {text}");
            if (LogList.Items.Count > 500)
                LogList.Items.RemoveAt(0);
            LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
        }

        private async void Load_Click(object? sender, RoutedEventArgs? e)
        {
            var dlg = new OpenFileDialog { Filter = "Видео|*.mp4;*.mkv;*.mov;*.avi" };
            if (dlg.ShowDialog() != true) return;
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

            _totalFrames = (long)Math.Round(_duration * _fps);
            _current = TimeSpan.Zero;
            _currentFrame = 0;

            Timeline.SetDuration(_duration);
            Timeline.SetCurrentTime(_current);
            Timeline.SetFrameInfo(_currentFrame, _totalFrames);

            await ShowFrameByCurrentFrame();

            FileNameText.Text = Path.GetFileName(path);
            UpdateInfoUI();
            SetIdleState();
            _isHandlingPlaybackEnd = false;
            Log($"Loaded file: {path}");
        }

        private void PlayFrom(TimeSpan time)
        {
            if (_file == null) return;

            _player.Stop();
            _current = ClampToDuration(time);
            _currentFrame = TimeToFrame(_current);

            _player.Start(_file, 1280, 720, _fps, _current, Speed);

            _isPlaying = true;
            _isPaused = false;
            SetPlayState();
            Log($"Play from {_current:hh\\:mm\\:ss\\.fff} at x{Speed}");
        }

        private void TogglePlayPause_Click(object? sender, RoutedEventArgs? e)
        {
            if (_file == null) return;

            if (_isPlaying)
            {
                _player.Pause();
                _isPlaying = false;
                _isPaused = true;
                SetPauseState();
                Log("Paused.");
                return;
            }

            PlayFrom(_current);
            Log("Play/Resume from current position.");
        }

        private async void Stop_Click(object? sender, RoutedEventArgs? e)
        {
            _player.Stop();

            _current = TimeSpan.Zero;
            _currentFrame = 0;

            Timeline.SetCurrentTime(_current);
            Timeline.SetFrameInfo(_currentFrame, _totalFrames);
            await ShowFrameByCurrentFrame();

            SetIdleState();
            _isHandlingPlaybackEnd = false;
            Log("Stopped.");
        }

        private async void Timeline_Changed(TimeSpan t)
        {
            _current = ClampToDuration(t);
            _currentFrame = TimeToFrame(_current);
            Timeline.SetFrameInfo(_currentFrame, _totalFrames);

            if (_isPlaying) PlayFrom(_current);
            else await ShowFrameByCurrentFrame();
        }

        private async System.Threading.Tasks.Task ShowFrameByCurrentFrame()
        {
            if (_file == null) return;

            var frame = await _grabber.GetFrame(_file, _current, 1280, 720);
            if (frame != null) Preview.SetFrame(frame);
        }

        private async void Step(int frames)
        {
            if (_file == null) return;

            _player.Stop();
            _currentFrame = Math.Clamp(_currentFrame + frames, 0, _totalFrames);
            _current = FrameToTime(_currentFrame);

            Timeline.SetCurrentTime(_current);
            Timeline.SetFrameInfo(_currentFrame, _totalFrames);
            await ShowFrameByCurrentFrame();

            _isPlaying = false;
            _isPaused = true;
            SetPauseState();
            Log($"Step to frame {_currentFrame}.");
        }

        private void SpeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SpeedCombo.SelectedItem is ComboBoxItem item)
            {
                var text = item.Content?.ToString()?.Replace("x", "");
                if (int.TryParse(text, out var val))
                {
                    var idx = Array.IndexOf(_speeds, (double)val);
                    if (idx >= 0) _speedIndex = idx;
                    UpdateSpeedUI();
                    if (_isPlaying) PlayFrom(_current);
                    Log($"Speed set to x{Speed}");
                }
            }
        }

        private void IncreaseSpeedHotkey()
        {
            if (_speedIndex < _speeds.Length - 1)
                _speedIndex++;

            SpeedCombo.SelectedIndex = _speedIndex;
            UpdateSpeedUI();
            if (_isPlaying) PlayFrom(_current);
        }

        private void ResetSpeedHotkey()
        {
            _speedIndex = 0;
            SpeedCombo.SelectedIndex = 0;
            UpdateSpeedUI();
            if (_isPlaying) PlayFrom(_current);
        }

        private void UpdateSpeedUI() => SpeedText.Text = $"x{Speed}";
        private void SetPlayState() { PlayIcon.Text = "▶"; PlayIcon.Foreground = System.Windows.Media.Brushes.Green; }
        private void SetPauseState() { PlayIcon.Text = "⏸"; PlayIcon.Foreground = System.Windows.Media.Brushes.Yellow; }
        private void SetIdleState() { PlayIcon.Text = "▶"; PlayIcon.Foreground = System.Windows.Media.Brushes.White; _isPlaying = false; _isPaused = false; }

        private void UpdateInfoUI()
        {
            ResolutionText.Text = $"Resolution: {_width}x{_height}";
            FpsText.Text = $"FPS: {_fps:0.##}";
            CodecText.Text = $"Codec: {_codec}";
            DurationText.Text = $"Duration: {TimeSpan.FromSeconds(_duration):hh\\:mm\\:ss} / {_totalFrames} frames";
        }

        private async void HandlePlaybackEnd()
        {
            if (_isHandlingPlaybackEnd) return;
            _isHandlingPlaybackEnd = true;

            _player.Stop();

            _current = TimeSpan.FromSeconds(_duration);
            _currentFrame = _totalFrames;
            Timeline.SetCurrentTime(_current);
            Timeline.SetFrameInfo(_currentFrame, _totalFrames);
            await ShowFrameByCurrentFrame();

            if (LoopCheck.IsChecked == true)
            {
                _isHandlingPlaybackEnd = false;
                _current = TimeSpan.Zero;
                _currentFrame = 0;
                PlayFrom(_current);
                return;
            }

            _isPlaying = false;
            _isPaused = true;
            SetPauseState();
            _isHandlingPlaybackEnd = false;
            Log("Playback ended.");
        }

        private TimeSpan ClampToDuration(TimeSpan value)
        {
            if (value < TimeSpan.Zero) return TimeSpan.Zero;
            var max = TimeSpan.FromSeconds(_duration);
            return value > max ? max : value;
        }

        private long TimeToFrame(TimeSpan time)
        {
            if (_fps <= 0) return 0;
            var frame = (long)Math.Round(time.TotalSeconds * _fps);
            return Math.Clamp(frame, 0, _totalFrames);
        }

        private TimeSpan FrameToTime(long frame)
        {
            if (_fps <= 0) return TimeSpan.Zero;
            return TimeSpan.FromSeconds(frame / _fps);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space) { TogglePlayPause_Click(null, null); e.Handled = true; return; }
            if (e.Key == Key.K) { if (_isPlaying) { _player.Pause(); _isPlaying = false; _isPaused = true; SetPauseState(); Log("Paused (K)."); } e.Handled = true; return; }
            if (e.Key == Key.S) { Stop_Click(null, null); e.Handled = true; return; }
            if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control) { Load_Click(null, null); e.Handled = true; return; }
            if (e.Key == Key.L) { IncreaseSpeedHotkey(); e.Handled = true; return; }
            if (e.Key == Key.J) { ResetSpeedHotkey(); e.Handled = true; return; }
            if (e.Key == Key.Right) { Step(Keyboard.Modifiers == ModifierKeys.Shift ? 10 : 1); e.Handled = true; return; }
            if (e.Key == Key.Left) { Step(Keyboard.Modifiers == ModifierKeys.Shift ? -10 : -1); e.Handled = true; return; }
            if (e.Key == Key.R) { LoopCheck.IsChecked = !(LoopCheck.IsChecked ?? false); e.Handled = true; }
        }

        private async void Window_Drop(object? sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length == 0) return;
            await LoadFile(files[0]);
        }

        private void PlaylistBox_SelectionChanged(object? sender, SelectionChangedEventArgs e) { }
    }
}