using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using AI_Video_ToolKit.UI.Services;

namespace AI_Video_ToolKit.UI
{
    public partial class MainWindow : Window
    {
        private sealed record Segment(int Index, TimeSpan Start, TimeSpan End, long StartFrame, long EndFrame)
        {
            public TimeSpan Duration => End - Start;
            public override string ToString() => $"{Index:000}_{Start:hh\\:mm\\:ss\\.fff}_{End:hh\\:mm\\:ss\\.fff} ({Duration:hh\\:mm\\:ss\\.fff})";
        }

        private enum MarkerActionType { InputSet, OutputSet, CutAdd, CutClear }
        private readonly record struct MarkerAction(MarkerActionType Type, TimeSpan? Value, List<TimeSpan>? SnapshotCuts = null);

        private readonly BufferedVideoPlayer _player = new();
        private readonly FFprobeService _ffprobe = new();
        private readonly FrameGrabber _grabber = new();

        private string? _file;
        private double _duration;
        private double _fps = 25;

        private TimeSpan _current = TimeSpan.Zero;
        private long _currentFrame;

        private bool _isPlaying;
        private bool _isHandlingPlaybackEnd;

        private readonly double[] _speeds = { 1, 2, 4, 8, 16 };
        private int _speedIndex;
        private double Speed => _speeds[_speedIndex];

        private int _width;
        private int _height;
        private long _totalFrames;
        private string _codec = "";
        private long _videoBitrate;

        private TimeSpan? _inputMarker;
        private TimeSpan? _outputMarker;
        private readonly List<TimeSpan> _cutMarkers = new();
        private readonly Stack<MarkerAction> _undoStack = new();
        private readonly List<Segment> _segments = new();
        private Segment? _selectedSegment;

        private TimeSpan _playbackRangeStart = TimeSpan.Zero;
        private TimeSpan _playbackRangeEnd = TimeSpan.MaxValue;

        private TimeSpan? _inputMarker;
        private TimeSpan? _outputMarker;
        private readonly List<TimeSpan> _cutMarkers = new();
        private readonly Stack<MarkerAction> _undoStack = new();
        private readonly List<Segment> _segments = new();

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

                if (_isPlaying && _current >= _playbackRangeEnd)
                {
                    _player.Pause();
                    _isPlaying = false;
                    SetPauseState();
                }
            });
            _player.OnPlaybackEnded += () => Dispatcher.Invoke(HandlePlaybackEnd);

            Timeline.OnChanged += Timeline_Changed;
            Log("MainWindow initialized.");
        }

        private void Log(string text)
        {
            LogList.Items.Add($"[{DateTime.Now:HH:mm:ss}] {text}");
            if (LogList.Items.Count > 500) LogList.Items.RemoveAt(0);
            LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
        }

        private void RefreshMarkers()
        {
            Timeline.SetMarkers(_inputMarker, _outputMarker, _cutMarkers);
            RebuildSegments();
        }

        private void RebuildSegments()
        {
            _segments.Clear();
            SegmentList.Items.Clear();
            if (_duration <= 0) return;

            var startBound = _inputMarker ?? TimeSpan.Zero;
            var endBound = _outputMarker ?? TimeSpan.FromSeconds(_duration);
            if (endBound <= startBound) return;

            var points = new List<TimeSpan> { startBound };
            points.AddRange(_cutMarkers.Where(c => c > startBound && c < endBound).OrderBy(x => x));
            points.Add(endBound);

            points = points.Distinct().OrderBy(x => x).ToList();

            int idx = 1;
            for (int i = 0; i < points.Count - 1; i++)
            {
                if (points[i + 1] <= points[i]) continue;
                var sf = TimeToFrame(points[i]);
                var ef = TimeToFrame(points[i + 1]);
                var seg = new Segment(idx++, points[i], points[i + 1], sf, ef);
                _segments.Add(seg);
                SegmentList.Items.Add(seg.ToString());
            }

            if (_selectedSegment != null)
            {
                _selectedSegment = _segments.FirstOrDefault(s => s.Index == _selectedSegment.Index);
            }

            Log($"Segments rebuilt: {_segments.Count}");
        }

        private (TimeSpan start, TimeSpan end) ResolvePlaybackRange()
        {
            if (_selectedSegment != null)
                return (_selectedSegment.Start, _selectedSegment.End);

            var start = _inputMarker ?? TimeSpan.Zero;
            var end = _outputMarker ?? TimeSpan.FromSeconds(_duration);
            if (end <= start) end = TimeSpan.FromSeconds(_duration);
            return (start, end);
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
            _videoBitrate = info.videoBitrate;

            _totalFrames = (long)Math.Round(_duration * _fps);
            _current = TimeSpan.Zero;
            _currentFrame = 0;

            _inputMarker = null;
            _outputMarker = null;
            _cutMarkers.Clear();
            _undoStack.Clear();
            _selectedSegment = null;

            Timeline.SetDuration(_duration);
            Timeline.SetCurrentTime(_current);
            Timeline.SetFrameInfo(_currentFrame, _totalFrames);
            RefreshMarkers();

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

            var range = ResolvePlaybackRange();
            _playbackRangeStart = range.start;
            _playbackRangeEnd = range.end;

            var start = ClampToDuration(time);
            if (start < _playbackRangeStart || start >= _playbackRangeEnd)
                start = _playbackRangeStart;

            _player.Stop();
            _current = start;
            _currentFrame = TimeToFrame(_current);
            _player.Start(_file, 1280, 720, _fps, _current, Speed);
            _isPlaying = true;
            SetPlayState();
        }

        private void TogglePlayPause_Click(object? sender, RoutedEventArgs? e)
        {
            if (_file == null) return;
            if (_isPlaying) { _player.Pause(); _isPlaying = false; SetPauseState(); return; }
            PlayFrom(_current);
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
            SetPauseState();
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
                }
            }
        }

        private async void Cut_Click(object sender, RoutedEventArgs e)
        {
            foreach (var seg in _segments) await ExportSegment(seg);
            Log($"Export all complete: {_segments.Count} segments");
        }

        private async void ExportSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSegment == null) return;
            await ExportSegment(_selectedSegment);
            Log($"Export selected complete: {_selectedSegment}");
        }

        private async System.Threading.Tasks.Task ExportSegment(Segment seg)
        {
            if (_file == null) return;
            var root = Directory.GetCurrentDirectory();
            var cutDir = Path.Combine(root, "Cut");
            Directory.CreateDirectory(cutDir);

            var srcName = Path.GetFileNameWithoutExtension(_file);
            var ext = Path.GetExtension(_file);
            var outFile = Path.Combine(cutDir, $"{seg.Index:000}_{srcName}_{seg.StartFrame}_{seg.EndFrame}{ext}");

            var startTime = seg.Start.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var endTime = seg.End.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var bitrateKbps = Math.Max(1500, (int)Math.Round((_videoBitrate > 0 ? _videoBitrate : 4_000_000) / 1000.0));
            var bufSizeKbps = bitrateKbps * 2;
            var ok = await RunFfmpeg($"-y -ss {startTime} -to {endTime} -i \"{_file}\" -map 0:v:0? -map 0:a? -sn -dn -c:v libx264 -preset veryfast -b:v {bitrateKbps}k -minrate {bitrateKbps}k -maxrate {bitrateKbps}k -bufsize {bufSizeKbps}k -c:a copy -movflags +faststart \"{outFile}\"");
            if (!ok)
            {
                if (File.Exists(outFile)) File.Delete(outFile);
                Log($"copy cut failed for segment {seg.Index}. Output removed.");
            }
        }

        private async void PreviewSegment_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSegment == null) return;
            _current = _selectedSegment.Start;
            _currentFrame = _selectedSegment.StartFrame;
            Timeline.SetCurrentTime(_current);
            Timeline.SetFrameInfo(_currentFrame, _totalFrames);
            await ShowFrameByCurrentFrame();
            if (_isPlaying) PlayFrom(_current);
        }

        private void SegmentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedSegment = SegmentList.SelectedIndex >= 0 && SegmentList.SelectedIndex < _segments.Count ? _segments[SegmentList.SelectedIndex] : null;
        }

        private void UndoMarker_Click(object sender, RoutedEventArgs e) => UndoMarker();
        private void ClearCuts_Click(object sender, RoutedEventArgs e)
        {
            if (_cutMarkers.Count == 0) return;
            _undoStack.Push(new MarkerAction(MarkerActionType.CutClear, null, new List<TimeSpan>(_cutMarkers)));
            _cutMarkers.Clear();
            RefreshMarkers();
        }

        private void UndoMarker()
        {
            if (_undoStack.Count == 0) return;
            var action = _undoStack.Pop();
            switch (action.Type)
            {
                case MarkerActionType.InputSet: _inputMarker = action.Value; break;
                case MarkerActionType.OutputSet: _outputMarker = action.Value; break;
                case MarkerActionType.CutAdd: if (action.Value.HasValue) _cutMarkers.Remove(action.Value.Value); break;
                case MarkerActionType.CutClear: _cutMarkers.Clear(); if (action.SnapshotCuts != null) _cutMarkers.AddRange(action.SnapshotCuts); break;
            }
            RefreshMarkers();
        }

        private static async System.Threading.Tasks.Task<bool> RunFfmpeg(string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = @"C:\_Portable_\ffmpeg\bin\ffmpeg.exe",
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var p = Process.Start(psi);
            if (p == null) return false;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }

        private void IncreaseSpeedHotkey() { if (_speedIndex < _speeds.Length - 1) _speedIndex++; SpeedCombo.SelectedIndex = _speedIndex; UpdateSpeedUI(); if (_isPlaying) PlayFrom(_current); }
        private void ResetSpeedHotkey() { _speedIndex = 0; SpeedCombo.SelectedIndex = 0; UpdateSpeedUI(); if (_isPlaying) PlayFrom(_current); }
        private void UpdateSpeedUI() => SpeedText.Text = $"x{Speed}";
        private void SetPlayState() { PlayIcon.Text = "▶"; PlayIcon.Foreground = System.Windows.Media.Brushes.Green; }
        private void SetPauseState() { PlayIcon.Text = "⏸"; PlayIcon.Foreground = System.Windows.Media.Brushes.Yellow; }
        private void SetIdleState() { PlayIcon.Text = "▶"; PlayIcon.Foreground = System.Windows.Media.Brushes.White; _isPlaying = false; }

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
            _current = _playbackRangeEnd <= TimeSpan.FromSeconds(_duration) ? _playbackRangeEnd : TimeSpan.FromSeconds(_duration);
            _currentFrame = TimeToFrame(_current);
            Timeline.SetCurrentTime(_current);
            Timeline.SetFrameInfo(_currentFrame, _totalFrames);
            await ShowFrameByCurrentFrame();
            if (LoopCheck.IsChecked == true)
            {
                _isHandlingPlaybackEnd = false;
                PlayFrom(_playbackRangeStart);
                return;
            }
            _isPlaying = false;
            SetPauseState();
            _isHandlingPlaybackEnd = false;
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
            if (e.Key == Key.K) { if (_isPlaying) { _player.Pause(); _isPlaying = false; SetPauseState(); } e.Handled = true; return; }
            if (e.Key == Key.S) { Stop_Click(null, null); e.Handled = true; return; }
            if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control) { Load_Click(null, null); e.Handled = true; return; }
            if (e.Key == Key.L) { IncreaseSpeedHotkey(); e.Handled = true; return; }
            if (e.Key == Key.J) { ResetSpeedHotkey(); e.Handled = true; return; }
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) { UndoMarker(); e.Handled = true; return; }
            if (e.Key == Key.I)
            {
                _undoStack.Push(new MarkerAction(MarkerActionType.InputSet, _inputMarker));
                _inputMarker = _current;
                _cutMarkers.RemoveAll(c => c <= _inputMarker.Value);
                RefreshMarkers();
                e.Handled = true; return;
            }
            if (e.Key == Key.O)
            {
                _undoStack.Push(new MarkerAction(MarkerActionType.OutputSet, _outputMarker));
                _outputMarker = _current;
                _cutMarkers.RemoveAll(c => c >= _outputMarker.Value);
                RefreshMarkers();
                e.Handled = true; return;
            }
            if (e.Key == Key.C)
            {
                var p = _current;
                if (_inputMarker.HasValue && p <= _inputMarker.Value) return;
                if (_outputMarker.HasValue && p >= _outputMarker.Value) return;
                _cutMarkers.Add(p);
                _cutMarkers.Sort();
                _undoStack.Push(new MarkerAction(MarkerActionType.CutAdd, p));
                RefreshMarkers();
                e.Handled = true; return;
            }
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
