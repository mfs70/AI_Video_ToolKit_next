#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using AI_Video_ToolKit.UI.Services;

namespace AI_Video_ToolKit.UI
{
    /// <summary>
    /// Главное окно приложения AI Video Toolkit
    /// Поддерживает:
    /// - Множественную загрузку видео и изображений (кнопка, Ctrl+L, Drag&Drop)
    /// - Плейлист (25% ширины слева)
    /// - Монтажный стол (внизу)
    /// - Нарезку видео маркерами (I, O, C, Delete, Undo)
    /// - Экспорт отрезков
    /// - Воспроизведение видео с аудио (через FFmpeg → стерео PCM)
    /// - Отображение полных параметров аудио (кодек, частота, каналы, битрейт)
    /// - Горячие клавиши: J/L - скорость, K - стоп, Space - плей/пауза, A - аудио, V - видео, M - склейка всех, R - повтор,
    ///   I/O/C - маркеры, Ctrl+Z - отмена, Delete - удалить маркер, Стрелки - шаг
    /// </summary>
    public partial class MainWindow : Window
    {
        #region ========== ВЛОЖЕННЫЕ ТИПЫ ==========

        private sealed record Segment(int Index, TimeSpan Start, TimeSpan End, long StartFrame, long EndFrame)
        {
            public TimeSpan Duration => End - Start;
            public override string ToString() => $"{Index:000}_{Start:hh\\:mm\\:ss\\.fff}_{End:hh\\:mm\\:ss\\.fff} ({Duration:hh\\:mm\\:ss\\.fff})";
        }

        private enum MarkerActionType { InputSet, OutputSet, CutAdd, CutClear }
        private readonly record struct MarkerAction(MarkerActionType Type, TimeSpan Value, List<TimeSpan> SnapshotCuts);

        public class PlaylistItem
        {
            public string FilePath { get; set; } = "";
            public string FileName => Path.GetFileName(FilePath);
            public string Extension => Path.GetExtension(FilePath).ToLower();
            public bool IsVideo => Extension == ".mp4" || Extension == ".mkv" || Extension == ".mov" || Extension == ".avi" || Extension == ".webm";
            public bool IsImage => Extension == ".jpg" || Extension == ".jpeg" || Extension == ".png" || Extension == ".bmp" || Extension == ".gif";
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

        #endregion

        #region ========== ПОЛЯ И СВОЙСТВА ==========

        private readonly BufferedVideoPlayer _player = new();
        private readonly FFprobeService _ffprobe = new();
        private readonly FrameGrabber _grabber = new();

        private string _file;
        private double _duration;
        private double _fps = 25;
        private int _width, _height;
        private long _totalFrames;
        private string _codec = "";
        private long _videoBitrate;
        private bool _hasAudio;
        private string _audioCodec = "";
        private int _audioSampleRate;
        private int _audioChannels;
        private long _audioBitrate;

        private TimeSpan _current = TimeSpan.Zero;
        private long _currentFrame;
        private bool _isPlaying;
        private bool _isHandlingPlaybackEnd;
        private TimeSpan _playbackRangeStart = TimeSpan.Zero;
        private TimeSpan _playbackRangeEnd = TimeSpan.MaxValue;

        private readonly double[] _speeds = { 0.1, 0.25, 0.5, 1, 2, 4, 8, 16 };
        private int _speedIndex;
        private double Speed => _speeds[_speedIndex];

        private TimeSpan _inputMarker;
        private TimeSpan _outputMarker;
        private readonly List<TimeSpan> _cutMarkers = new();
        private readonly Stack<MarkerAction> _undoStack = new();
        private readonly List<Segment> _segments = new();
        private Segment _selectedSegment;

        private readonly List<PlaylistItem> _playlistItems = new();
        private int _currentPlaylistIndex = -1;

        private readonly List<MontageItem> _montageItems = new();

        #endregion

        #region ========== КОНСТРУКТОР ==========

        public MainWindow()
        {
            InitializeComponent();

            _player.LogCallback = Log;
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

            // Заполнение комбобокса скоростей
            SpeedCombo.Items.Clear();
            foreach (var s in _speeds)
                SpeedCombo.Items.Add($"{s}x");
            _speedIndex = Array.IndexOf(_speeds, 1.0);
            if (_speedIndex < 0) _speedIndex = 3;
            SpeedCombo.SelectedIndex = _speedIndex;

            AudioCheck.Checked += AudioCheck_CheckedChanged;
            AudioCheck.Unchecked += AudioCheck_CheckedChanged;
            VideoCheck.Checked += VideoCheck_CheckedChanged;
            VideoCheck.Unchecked += VideoCheck_CheckedChanged;

            Log("MainWindow initialized. Hotkeys: J/L - speed, K - stop, Space - play/pause, A - audio, V - video, M - merge all, R - loop, I/O/C - markers, Ctrl+Z - undo, Delete - delete marker, Arrows - step.");

            AllowDrop = true;
            Drop += Window_Drop;
        }

        private void Log(string text)
        {
            Dispatcher.Invoke(() =>
            {
                LogList.Items.Add($"[{DateTime.Now:HH:mm:ss}] {text}");
                if (LogList.Items.Count > 500) LogList.Items.RemoveAt(0);
                LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
            });
        }

        #endregion

        #region ========== МАРКЕРЫ И ОТРЕЗКИ ==========

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

            var startBound = _inputMarker != TimeSpan.Zero ? _inputMarker : TimeSpan.Zero;
            var endBound = _outputMarker != TimeSpan.Zero ? _outputMarker : TimeSpan.FromSeconds(_duration);
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
                _selectedSegment = _segments.FirstOrDefault(s => s.Index == _selectedSegment.Index);
            Log($"Segments rebuilt: {_segments.Count}");
        }

        private (TimeSpan start, TimeSpan end) ResolvePlaybackRange()
        {
            if (_selectedSegment != null) return (_selectedSegment.Start, _selectedSegment.End);
            var start = _inputMarker != TimeSpan.Zero ? _inputMarker : TimeSpan.Zero;
            var end = _outputMarker != TimeSpan.Zero ? _outputMarker : TimeSpan.FromSeconds(_duration);
            if (end <= start) end = TimeSpan.FromSeconds(_duration);
            return (start, end);
        }

        private void MoveSelectedMarkerByFrames(int frames)
        {
            if (Timeline.SelectedMarkerTime == null) return;
            var moved = ClampToDuration(FrameToTime(TimeToFrame(Timeline.SelectedMarkerTime.Value) + frames));
            if (Timeline.SelectedMarkerType == Controls.TimelineControl.MarkerSelection.Input)
                _inputMarker = moved;
            if (Timeline.SelectedMarkerType == Controls.TimelineControl.MarkerSelection.Output)
                _outputMarker = moved;
            if (Timeline.SelectedMarkerType == Controls.TimelineControl.MarkerSelection.Cut)
            {
                _cutMarkers.Remove(Timeline.SelectedMarkerTime.Value);
                _cutMarkers.Add(moved);
                _cutMarkers.Sort();
            }
            RefreshMarkers();
        }

        private void DeleteSelectedMarker()
        {
            if (Timeline.SelectedMarkerTime == null) return;
            if (Timeline.SelectedMarkerType == Controls.TimelineControl.MarkerSelection.Input) _inputMarker = TimeSpan.Zero;
            if (Timeline.SelectedMarkerType == Controls.TimelineControl.MarkerSelection.Output) _outputMarker = TimeSpan.Zero;
            if (Timeline.SelectedMarkerType == Controls.TimelineControl.MarkerSelection.Cut)
                _cutMarkers.Remove(Timeline.SelectedMarkerTime.Value);
            RefreshMarkers();
        }

        #endregion

        #region ========== ЗАГРУЗКА ФАЙЛОВ ==========

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
            _hasAudio = info.hasAudio;
            _audioCodec = info.audioCodec;
            _audioSampleRate = info.audioSampleRate;
            _audioChannels = info.audioChannels;
            _audioBitrate = info.audioBitrate;

            _totalFrames = (long)Math.Round(_duration * _fps);
            _current = TimeSpan.Zero;
            _currentFrame = 0;

            _inputMarker = TimeSpan.Zero;
            _outputMarker = TimeSpan.Zero;
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
            RefreshMontagePanel();
            SetIdleState();
            _isHandlingPlaybackEnd = false;
            Log($"Loaded file: {path}");
        }

        private void LoadFilesToPlaylist(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                if (!File.Exists(path)) continue;
                var ext = Path.GetExtension(path).ToLower();
                bool isValid = ext == ".mp4" || ext == ".mkv" || ext == ".mov" || ext == ".avi" ||
                               ext == ".webm" || ext == ".jpg" || ext == ".jpeg" || ext == ".png" ||
                               ext == ".bmp" || ext == ".gif";
                if (!isValid)
                {
                    Log($"Skipping unsupported file: {Path.GetFileName(path)}");
                    continue;
                }
                _playlistItems.Add(new PlaylistItem { FilePath = path });
                PlaylistListBox.Items.Add($"{_playlistItems.Last().TypeIcon} {Path.GetFileName(path)}");
            }
            PlaylistCount.Text = $"{_playlistItems.Count} items";
            Log($"Added {_playlistItems.Count} items to playlist");
        }

        private async void LoadFilesToMontage(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                if (!File.Exists(path)) continue;
                var ext = Path.GetExtension(path).ToLower();
                bool isValid = ext == ".mp4" || ext == ".mkv" || ext == ".mov" || ext == ".avi" ||
                               ext == ".webm" || ext == ".jpg" || ext == ".jpeg" || ext == ".png";
                if (!isValid) continue;

                var item = new MontageItem
                {
                    FilePath = path,
                    TypeIcon = (ext == ".mp4" || ext == ".mkv" || ext == ".mov" || ext == ".avi" || ext == ".webm") ? "🎬" : "🖼️"
                };
                if (item.TypeIcon == "🎬")
                {
                    try
                    {
                        var info = await _ffprobe.GetInfo(path);
                        item.Duration = TimeSpan.FromSeconds(info.duration);
                    }
                    catch (Exception ex) { Log($"Failed to get duration for {Path.GetFileName(path)}: {ex.Message}"); }
                }
                _montageItems.Add(item);
                MontageList.Items.Add($"{item.TypeIcon} {item.FileName} {(item.Duration.TotalSeconds > 0 ? $"({item.DurationStr})" : "")}");
            }
            MontageCount.Text = $"{_montageItems.Count} clips";
            Log($"Added {_montageItems.Count} items to montage table");
        }

        private void LoadFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return;
            var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase));
            LoadFilesToPlaylist(files);
            Log($"Loaded folder: {folderPath} ({files.Count()} files)");
        }

        private void UpdateInfoUI()
        {
            ResolutionText.Text = $"Resolution: {_width}x{_height}";
            FpsText.Text = $"FPS: {_fps:0.##}";
            CodecText.Text = $"Codec: {_codec}";
            BitrateText.Text = $"Bitrate: {Math.Round(_videoBitrate / 1000.0):0} kbps";
            DurationText.Text = $"Duration: {TimeSpan.FromSeconds(_duration):hh\\:mm\\:ss} / {_totalFrames} frames";

            if (_hasAudio)
            {
                string audioInfo = $"Audio: {_audioCodec} | {_audioSampleRate / 1000.0:F1} kHz | {_audioChannels} ch";
                if (_audioBitrate > 0)
                    audioInfo += $" | {Math.Round(_audioBitrate / 1000.0):0} kbps";
                AudioInfoText.Text = audioInfo;
            }
            else
            {
                AudioInfoText.Text = "Audio: none";
            }
        }

        #endregion

        #region ========== ВОСПРОИЗВЕДЕНИЕ ==========

        private void PlayFrom(TimeSpan time)
        {
            if (string.IsNullOrEmpty(_file)) return;
            var range = ResolvePlaybackRange();
            _playbackRangeStart = range.start;
            _playbackRangeEnd = range.end;
            var start = ClampToDuration(time);
            if (start < _playbackRangeStart || start >= _playbackRangeEnd)
                start = _playbackRangeStart;

            _player.Stop();
            _current = start;
            _currentFrame = TimeToFrame(_current);
            bool enableAudio = AudioCheck.IsChecked == true && _hasAudio;
            _player.Start(_file, 1280, 720, _fps, _current, Speed, enableAudio);
            _isPlaying = true;
            SetPlayState();
        }

        private async System.Threading.Tasks.Task ShowFrameByCurrentFrame()
        {
            if (string.IsNullOrEmpty(_file)) return;
            var frame = await _grabber.GetFrame(_file, _current, 1280, 720);
            if (frame != null) Preview.SetFrame(frame);
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

        private async void Step(int frames)
        {
            if (string.IsNullOrEmpty(_file)) return;
            _player.Stop();
            _currentFrame = Math.Clamp(_currentFrame + frames, 0, _totalFrames);
            _current = FrameToTime(_currentFrame);
            Timeline.SetCurrentTime(_current);
            Timeline.SetFrameInfo(_currentFrame, _totalFrames);
            await ShowFrameByCurrentFrame();
            _isPlaying = false;
            SetPauseState();
        }

        private void IncreaseSpeedHotkey()
        {
            if (_speedIndex < _speeds.Length - 1)
            {
                _speedIndex++;
                SpeedCombo.SelectedIndex = _speedIndex;
                if (_isPlaying) PlayFrom(_current);
                Log($"Speed increased to {_speeds[_speedIndex]}x");
            }
        }

        private void DecreaseSpeedHotkey()
        {
            if (_speedIndex > 0)
            {
                _speedIndex--;
                SpeedCombo.SelectedIndex = _speedIndex;
                if (_isPlaying) PlayFrom(_current);
                Log($"Speed decreased to {_speeds[_speedIndex]}x");
            }
        }

        #endregion

        #region ========== УПРАВЛЕНИЕ ПЛЕЙЛИСТОМ ==========

        private async void PlayPlaylistItem(int index)
        {
            if (index < 0 || index >= _playlistItems.Count) return;
            _currentPlaylistIndex = index;
            var item = _playlistItems[index];
            if (item.IsVideo)
            {
                await LoadFile(item.FilePath);
            }
            else if (item.IsImage)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(item.FilePath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    Preview.SetImage(bitmap);
                    FileNameText.Text = item.FileName;
                    Log($"Displaying image: {item.FileName}");
                }
                catch (Exception ex) { Log($"Failed to load image: {ex.Message}"); }
            }
            Log($"Playing from playlist: {item.FileName}");
        }

        #endregion

        #region ========== ОБРАБОТЧИКИ UI ==========

        private async void LoadMultiple_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Media files|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.jpg;*.jpeg;*.png;*.bmp;*.gif",
                Multiselect = true
            };
            if (dlg.ShowDialog() == true)
            {
                LoadFilesToPlaylist(dlg.FileNames);
                if (_playlistItems.Count > 0 && string.IsNullOrEmpty(_file))
                    await LoadFile(_playlistItems[0].FilePath);
            }
        }

        private async void Load_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Видео|*.mp4;*.mkv;*.mov;*.avi" };
            if (dlg.ShowDialog() != true) return;
            await LoadFile(dlg.FileName);
        }

        private void TogglePlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_file) && _playlistItems.Count > 0)
            {
                PlayPlaylistItem(0);
                return;
            }
            if (string.IsNullOrEmpty(_file)) return;
            if (_isPlaying)
            {
                _player.Pause();
                _isPlaying = false;
                SetPauseState();
                return;
            }
            PlayFrom(_current);
        }

        private async void Stop_Click(object sender, RoutedEventArgs e)
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

        private void Previous_Click(object sender, RoutedEventArgs e)
        {
            if (_playlistItems.Count == 0) return;
            _currentPlaylistIndex--;
            if (_currentPlaylistIndex < 0) _currentPlaylistIndex = _playlistItems.Count - 1;
            PlayPlaylistItem(_currentPlaylistIndex);
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_playlistItems.Count == 0) return;
            _currentPlaylistIndex++;
            if (_currentPlaylistIndex >= _playlistItems.Count) _currentPlaylistIndex = 0;
            PlayPlaylistItem(_currentPlaylistIndex);
        }

        private void SpeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SpeedCombo.SelectedIndex >= 0 && SpeedCombo.SelectedIndex < _speeds.Length)
            {
                _speedIndex = SpeedCombo.SelectedIndex;
                if (_isPlaying) PlayFrom(_current);
            }
        }

        private async void Timeline_Changed(TimeSpan t)
        {
            _current = ClampToDuration(t);
            _currentFrame = TimeToFrame(_current);
            Timeline.SetFrameInfo(_currentFrame, _totalFrames);
            if (_isPlaying) PlayFrom(_current);
            else await ShowFrameByCurrentFrame();
        }

        private void SegmentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedSegment = SegmentList.SelectedIndex >= 0 && SegmentList.SelectedIndex < _segments.Count
                ? _segments[SegmentList.SelectedIndex] : null;
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

        private void UndoMarker_Click(object sender, RoutedEventArgs e) => UndoMarker();

        private void ClearCuts_Click(object sender, RoutedEventArgs e)
        {
            if (_cutMarkers.Count == 0) return;
            _undoStack.Push(new MarkerAction(MarkerActionType.CutClear, TimeSpan.Zero, new List<TimeSpan>(_cutMarkers)));
            _cutMarkers.Clear();
            RefreshMarkers();
        }

        private async void Cut_Click(object sender, RoutedEventArgs e)
        {
            ExportProgress.Value = 0;
            var total = Math.Max(1, _segments.Count);
            for (int i = 0; i < _segments.Count; i++)
            {
                await ExportSegment(_segments[i]);
                ExportProgress.Value = (i + 1) * 100.0 / total;
            }
            RefreshMontagePanel();
            Log($"Export all complete: {_segments.Count} segments");
        }

        private async void ExportSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSegment == null) return;
            ExportProgress.Value = 0;
            await ExportSegment(_selectedSegment);
            ExportProgress.Value = 100;
            RefreshMontagePanel();
            Log($"Export selected complete: {_selectedSegment}");
        }

        private async System.Threading.Tasks.Task ExportSegment(Segment seg)
        {
            if (string.IsNullOrEmpty(_file)) return;
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
            var processVideo = VideoCheck.IsChecked == true;
            var processAudio = AudioCheck.IsChecked == true;
            if (!processVideo && !processAudio)
            {
                Log("Export skipped: both Video and Audio are disabled.");
                return;
            }
            var args = processVideo
                ? $"-y -ss {startTime} -to {endTime} -i \"{_file}\" -map 0:v:0? {(processAudio ? "-map 0:a?" : "")} -sn -dn -c:v libx264 -preset veryfast -b:v {bitrateKbps}k -minrate {bitrateKbps}k -maxrate {bitrateKbps}k -bufsize {bufSizeKbps}k {(processAudio ? "-c:a copy" : "-an")} -movflags +faststart \"{outFile}\""
                : $"-y -ss {startTime} -to {endTime} -i \"{_file}\" -map 0:a? -vn -c:a copy \"{outFile}\"";
            var ok = await RunFfmpeg(args);
            if (!ok && File.Exists(outFile)) File.Delete(outFile);
            if (!ok) Log($"copy cut failed for segment {seg.Index}. Output removed.");
        }

        private async void MergeSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = MontageList.SelectedItems.Cast<string>().ToList();
            if (selected.Count < 2) { Log("Select at least 2 montage items."); return; }
            await MergeFiles(selected);
        }

        private async void MergeAll_Click(object sender, RoutedEventArgs e)
        {
            var allItems = MontageList.Items.Cast<string>().ToList();
            if (allItems.Count < 2) { Log("Need at least 2 clips in montage table."); return; }
            await MergeFiles(allItems);
        }

        private async System.Threading.Tasks.Task MergeFiles(List<string> fileNames)
        {
            var root = Directory.GetCurrentDirectory();
            var cutDir = Path.Combine(root, "Cut");
            var outDir = Path.Combine(root, "Output");
            Directory.CreateDirectory(outDir);
            var listFile = Path.Combine(root, "Temp", "concat_list.txt");
            Directory.CreateDirectory(Path.Combine(root, "Temp"));
            File.WriteAllLines(listFile, fileNames.Select(s => $"file '{Path.Combine(cutDir, s).Replace("'", "''")}'"));
            var outFile = Path.Combine(outDir, $"merged_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
            var ok = await RunFfmpeg($"-y -f concat -safe 0 -i \"{listFile}\" -c copy \"{outFile}\"");
            Log(ok ? $"Merged to {outFile}" : "Merge failed");
        }

        #endregion

        #region ========== ОБРАБОТЧИКИ ЧЕКБОКСОВ ==========

        private void AudioCheck_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_file)) return;
            bool enable = AudioCheck.IsChecked == true;
            if (enable && !_hasAudio)
            {
                Log("Audio cannot be enabled: no audio stream in file.");
                AudioCheck.IsChecked = false;
                return;
            }
            if (_isPlaying)
                PlayFrom(_current);
            else
            {
                _player.Stop();
                _player.Start(_file, 1280, 720, _fps, _current, Speed, enable);
            }
            Log(enable ? "Audio enabled." : "Audio disabled.");
        }

        private void VideoCheck_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_file)) return;
            bool enableAudio = AudioCheck.IsChecked == true && _hasAudio;
            if (_isPlaying)
                PlayFrom(_current);
            else
            {
                _player.Stop();
                _player.Start(_file, 1280, 720, _fps, _current, Speed, enableAudio);
            }
            Log(VideoCheck.IsChecked == true ? "Video enabled." : "Video disabled.");
        }

        #endregion

        #region ========== DRAG & DROP ОБРАБОТЧИКИ ==========

        private void Playlist_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var items = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (items == null || items.Length == 0) return;

            var files = new List<string>();
            foreach (var item in items)
            {
                if (Directory.Exists(item))
                {
                    var dirFiles = Directory.GetFiles(item, "*.*", SearchOption.AllDirectories)
                        .Where(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase));
                    files.AddRange(dirFiles);
                }
                else if (File.Exists(item)) files.Add(item);
            }
            files = files.Distinct().ToList();
            if (files.Count > 0) LoadFilesToPlaylist(files);
            e.Handled = true;
        }

        private void Playlist_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Player_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var items = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (items == null || items.Length == 0) return;

            var files = new List<string>();
            foreach (var item in items)
            {
                if (Directory.Exists(item))
                {
                    var dirFiles = Directory.GetFiles(item, "*.*", SearchOption.AllDirectories)
                        .Where(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase));
                    files.AddRange(dirFiles);
                }
                else if (File.Exists(item)) files.Add(item);
            }
            files = files.Distinct().ToList();
            if (files.Count > 0)
            {
                LoadFilesToPlaylist(files);
                _ = LoadFile(files[0]);
            }
            e.Handled = true;
        }

        private void Player_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void MontageTable_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var items = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (items == null || items.Length == 0) return;

            var files = new List<string>();
            foreach (var item in items)
            {
                if (Directory.Exists(item))
                {
                    var dirFiles = Directory.GetFiles(item, "*.*", SearchOption.AllDirectories)
                        .Where(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
                    files.AddRange(dirFiles);
                }
                else if (File.Exists(item)) files.Add(item);
            }
            files = files.Distinct().ToList();
            if (files.Count > 0) LoadFilesToMontage(files);
            e.Handled = true;
        }

        private void MontageTable_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void MontageList_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var items = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (items == null || items.Length == 0) return;

            var files = new List<string>();
            foreach (var item in items)
            {
                if (Directory.Exists(item))
                {
                    var dirFiles = Directory.GetFiles(item, "*.*", SearchOption.AllDirectories)
                        .Where(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
                    files.AddRange(dirFiles);
                }
                else if (File.Exists(item)) files.Add(item);
            }
            files = files.Distinct().ToList();
            if (files.Count > 0) LoadFilesToMontage(files);
            e.Handled = true;
        }

        private void MontageList_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Handled) return;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var items = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (items == null || items.Length == 0) return;

            var files = new List<string>();
            foreach (var item in items)
            {
                if (Directory.Exists(item)) LoadFolder(item);
                else if (File.Exists(item)) files.Add(item);
            }
            files = files.Distinct().ToList();
            if (files.Count > 0)
            {
                LoadFilesToPlaylist(files);
                if (_playlistItems.Count > 0 && string.IsNullOrEmpty(_file))
                    await LoadFile(_playlistItems[0].FilePath);
            }
            e.Handled = true;
        }

        #endregion

        #region ========== УПРАВЛЕНИЕ ПЛЕЙЛИСТОМ UI ==========

        private void ClearPlaylist_Click(object sender, RoutedEventArgs e)
        {
            _playlistItems.Clear();
            PlaylistListBox.Items.Clear();
            PlaylistCount.Text = "0 items";
            _currentPlaylistIndex = -1;
            Log("Playlist cleared");
        }

        private void RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var idx = PlaylistListBox.SelectedIndex;
            if (idx >= 0 && idx < _playlistItems.Count)
            {
                _playlistItems.RemoveAt(idx);
                PlaylistListBox.Items.RemoveAt(idx);
                PlaylistCount.Text = $"{_playlistItems.Count} items";
                if (_currentPlaylistIndex >= idx) _currentPlaylistIndex--;
                Log("Removed item from playlist");
            }
        }

        private async void Playlist_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PlaylistListBox.SelectedIndex >= 0 && PlaylistListBox.SelectedIndex < _playlistItems.Count)
            {
                _currentPlaylistIndex = PlaylistListBox.SelectedIndex;
                await LoadFile(_playlistItems[_currentPlaylistIndex].FilePath);
            }
        }

        #endregion

        #region ========== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ==========

        private void UndoMarker()
        {
            if (_undoStack.Count == 0) return;
            var action = _undoStack.Pop();
            switch (action.Type)
            {
                case MarkerActionType.InputSet: _inputMarker = action.Value; break;
                case MarkerActionType.OutputSet: _outputMarker = action.Value; break;
                case MarkerActionType.CutAdd: if (action.Value != TimeSpan.Zero) _cutMarkers.Remove(action.Value); break;
                case MarkerActionType.CutClear: _cutMarkers.Clear(); if (action.SnapshotCuts != null) _cutMarkers.AddRange(action.SnapshotCuts); break;
            }
            RefreshMarkers();
        }

        private void SetPlayState()
        {
            PlayIcon.Text = "⏸";
            PlayIcon.Foreground = Brushes.Yellow;
            StatusText.Text = "▶ Playing";
        }

        private void SetPauseState()
        {
            PlayIcon.Text = "▶";
            PlayIcon.Foreground = Brushes.LightGreen;
            StatusText.Text = "⏸ Paused";
        }

        private void SetIdleState()
        {
            PlayIcon.Text = "▶";
            PlayIcon.Foreground = Brushes.White;
            _isPlaying = false;
            StatusText.Text = "✅ Ready";
        }

        private void RefreshMontagePanel()
        {
            MontageList.Items.Clear();
            var cutDir = Path.Combine(Directory.GetCurrentDirectory(), "Cut");
            if (!Directory.Exists(cutDir)) return;
            foreach (var f in Directory.GetFiles(cutDir).OrderBy(x => x))
                MontageList.Items.Add(Path.GetFileName(f));
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

        #endregion

        #region ========== ГОРЯЧИЕ КЛАВИШИ ==========

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Пробел – Play/Pause
            if (e.Key == Key.Space) { TogglePlayPause_Click(sender, e); e.Handled = true; return; }

            // K – Stop
            if (e.Key == Key.K) { Stop_Click(sender, e); e.Handled = true; return; }

            // S – также Stop (для удобства)
            if (e.Key == Key.S) { Stop_Click(sender, e); e.Handled = true; return; }

            // Ctrl+L – загрузка файлов
            if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control) { LoadMultiple_Click(sender, e); e.Handled = true; return; }

            // L – увеличение скорости
            if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.None) { IncreaseSpeedHotkey(); e.Handled = true; return; }

            // J – уменьшение скорости
            if (e.Key == Key.J && Keyboard.Modifiers == ModifierKeys.None) { DecreaseSpeedHotkey(); e.Handled = true; return; }

            // Ctrl+Z – отмена маркера
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) { UndoMarker(); e.Handled = true; return; }

            // I – входной маркер (IN)
            if (e.Key == Key.I)
            {
                _undoStack.Push(new MarkerAction(MarkerActionType.InputSet, _inputMarker, null));
                _inputMarker = _current;
                _cutMarkers.RemoveAll(c => c <= _inputMarker);
                RefreshMarkers();
                e.Handled = true;
                return;
            }

            // O – выходной маркер (OUT)
            if (e.Key == Key.O)
            {
                _undoStack.Push(new MarkerAction(MarkerActionType.OutputSet, _outputMarker, null));
                _outputMarker = _current;
                _cutMarkers.RemoveAll(c => c >= _outputMarker);
                RefreshMarkers();
                e.Handled = true;
                return;
            }

            // C – маркер обрезки (CUT)
            if (e.Key == Key.C)
            {
                var p = _current;
                if (_inputMarker != TimeSpan.Zero && p <= _inputMarker) return;
                if (_outputMarker != TimeSpan.Zero && p >= _outputMarker) return;
                _cutMarkers.Add(p);
                _cutMarkers.Sort();
                _undoStack.Push(new MarkerAction(MarkerActionType.CutAdd, p, null));
                RefreshMarkers();
                e.Handled = true;
                return;
            }

            // Delete – удалить выбранный маркер
            if (e.Key == Key.Delete) { DeleteSelectedMarker(); e.Handled = true; return; }

            // Стрелки – перемещение маркера или покадровый шаг
            if (e.Key == Key.Right)
            {
                if (Timeline.SelectedMarkerType != Controls.TimelineControl.MarkerSelection.None)
                    MoveSelectedMarkerByFrames(Keyboard.Modifiers == ModifierKeys.Shift ? 10 : 1);
                else Step(Keyboard.Modifiers == ModifierKeys.Shift ? 10 : 1);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Left)
            {
                if (Timeline.SelectedMarkerType != Controls.TimelineControl.MarkerSelection.None)
                    MoveSelectedMarkerByFrames(Keyboard.Modifiers == ModifierKeys.Shift ? -10 : -1);
                else Step(Keyboard.Modifiers == ModifierKeys.Shift ? -10 : -1);
                e.Handled = true;
                return;
            }

            // R – переключение повтора (Loop)
            if (e.Key == Key.R) { LoopCheck.IsChecked = !(LoopCheck.IsChecked ?? false); e.Handled = true; return; }

            // A – переключение аудиодорожки
            if (e.Key == Key.A) { AudioCheck.IsChecked = !(AudioCheck.IsChecked ?? false); e.Handled = true; return; }

            // V – переключение видеодорожки
            if (e.Key == Key.V) { VideoCheck.IsChecked = !(VideoCheck.IsChecked ?? false); e.Handled = true; return; }

            // M – склейка всех клипов на монтажном столе
            if (e.Key == Key.M) { MergeAll_Click(sender, e); e.Handled = true; return; }

            // F5 / F6 – навигация по плейлисту (для удобства)
            if (e.Key == Key.F5 && _playlistItems.Count > 0) { Previous_Click(sender, e); e.Handled = true; return; }
            if (e.Key == Key.F6 && _playlistItems.Count > 0) { Next_Click(sender, e); e.Handled = true; return; }
        }

        #endregion
    }
}