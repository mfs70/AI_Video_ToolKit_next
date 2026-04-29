using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        private List<string> _playlist = new();
        private int _index = -1;

        private double _fps;
        private TimeSpan _currentTime = TimeSpan.Zero;

        private TimeSpan _inPoint = TimeSpan.Zero;
        private TimeSpan _outPoint = TimeSpan.Zero;

        private bool _isPlaying = false;
        private bool _userInteracting = false;
        private bool _seekChangedWhilePaused = false;

        private CancellationTokenSource? _seekCts;

        public MainWindow()
        {
            InitializeComponent();

            _player.OnFrame += f =>
                Dispatcher.Invoke(() => Preview.SetFrame(f));

            _player.OnPositionChanged += t =>
            {
                _currentTime = t;

                if (_userInteracting) return;

                Dispatcher.Invoke(() =>
                    Timeline.SetCurrentTime(t));

                HandlePlaybackBounds(t);
            };

            Timeline.OnUserInteraction += b => _userInteracting = b;
            Timeline.OnTimeChanged += Timeline_OnTimeChanged;
        }

        // =========================
        // PLAYBACK BOUNDS (FIX LOOP + PLAYLIST)
        // =========================

        private void HandlePlaybackBounds(TimeSpan t)
        {
            if (_outPoint <= _inPoint) return;

            if (t < _outPoint) return;

            // LOOP
            if (LoopCheck.IsChecked == true)
            {
                _currentTime = _inPoint;
                RestartPlayback();
                return;
            }

            // NEXT FILE
            if (_index < _playlist.Count - 1)
            {
                _index++;
                Dispatcher.Invoke(async () =>
                {
                    RefreshPlaylist();
                    await LoadCurrent();
                });
            }
            else
            {
                _player.Pause();
                _isPlaying = false;
            }
        }

        // =========================
        // LOAD
        // =========================

        private async void Load_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Multiselect = true };

            if (dlg.ShowDialog() != true)
                return;

            _playlist = dlg.FileNames.ToList();
            _index = 0;

            RefreshPlaylist();
            await LoadCurrent();
        }

        private async Task LoadCurrent()
        {
            if (_index < 0 || _index >= _playlist.Count)
                return;

            var file = _playlist[_index];

            FileNameText.Text = Path.GetFileName(file);

            _player.Stop();

            var info = await _ffprobe.GetInfo(file);

            _fps = info.fps;

            _currentTime = TimeSpan.Zero;
            _inPoint = TimeSpan.Zero;
            _outPoint = TimeSpan.FromSeconds(info.duration);

            Timeline.SetDuration(info.duration);
            Timeline.SetInOut(_inPoint, _outPoint);
            Timeline.SetCurrentTime(TimeSpan.Zero);

            // 🔥 ПОКАЗЫВАЕМ ПЕРВЫЙ КАДР
            var frame = await _grabber.GetFrame(file, _currentTime, 1280, 720);
            if (frame != null)
                Preview.SetFrame(frame);

            _isPlaying = true;
            StartPlayback();
        }

        private void RefreshPlaylist()
        {
            PlaylistBox.ItemsSource = null;
            PlaylistBox.ItemsSource = _playlist.Select(Path.GetFileName);
            PlaylistBox.SelectedIndex = _index;
        }

        // =========================
        // PLAY CONTROL (FIX)
        // =========================

        private void TogglePlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_index < 0) return;

            if (_isPlaying)
            {
                _player.Pause();
                _isPlaying = false;
                return;
            }

            // 🔥 если был seek — только restart
            if (_seekChangedWhilePaused)
            {
                _seekChangedWhilePaused = false;
                RestartPlayback();
                return;
            }

            _player.Resume();
            _isPlaying = true;
        }

        private void StartPlayback()
        {
            _player.Start(_playlist[_index], 1280, 720, _fps, _currentTime);
        }

        private void RestartPlayback()
        {
            _player.Stop();
            _player.Start(_playlist[_index], 1280, 720, _fps, _currentTime);
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            _player.Stop();
            _isPlaying = false;
            _currentTime = TimeSpan.Zero;

            Timeline.SetCurrentTime(_currentTime);
        }

        private void Prev_Click(object sender, RoutedEventArgs e)
        {
            if (_index > 0)
            {
                _index--;
                RefreshPlaylist();
                _ = LoadCurrent();
            }
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_index < _playlist.Count - 1)
            {
                _index++;
                RefreshPlaylist();
                _ = LoadCurrent();
            }
        }

        private void PlaylistBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (PlaylistBox.SelectedIndex >= 0)
            {
                _index = PlaylistBox.SelectedIndex;
                _ = LoadCurrent();
            }
        }

        // =========================
        // TIMELINE (FIX)
        // =========================

        private void Timeline_OnTimeChanged(TimeSpan time)
        {
            _currentTime = time;

            if (!_isPlaying)
                _seekChangedWhilePaused = true;

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
                        Dispatcher.Invoke(RestartPlayback);
                    }
                    else
                    {
                        var frame = await _grabber.GetFrame(
                            _playlist[_index],
                            _currentTime,
                            1280,
                            720);

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

        // =========================
        // HOTKEYS
        // =========================

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (_index < 0) return;

            if (e.Key == Key.Space)
            {
                TogglePlayPause_Click(null, null);
                e.Handled = true;
            }

            if (e.Key == Key.Left)
            {
                StepFrame(-1);
                e.Handled = true;
            }

            if (e.Key == Key.Right)
            {
                StepFrame(1);
                e.Handled = true;
            }

            if (e.Key == Key.I)
            {
                _inPoint = _currentTime;
                Timeline.SetInOut(_inPoint, _outPoint);
            }

            if (e.Key == Key.O)
            {
                _outPoint = _currentTime;
                Timeline.SetInOut(_inPoint, _outPoint);
            }
        }

        private async void StepFrame(int dir)
        {
            if (_fps <= 0) return;

            _currentTime += TimeSpan.FromSeconds(dir * (1.0 / _fps));

            if (_currentTime < _inPoint) _currentTime = _inPoint;
            if (_currentTime > _outPoint) _currentTime = _outPoint;

            _player.Pause();
            _isPlaying = false;
            _seekChangedWhilePaused = true;

            var frame = await _grabber.GetFrame(
                _playlist[_index],
                _currentTime,
                1280,
                720);

            if (frame != null)
            {
                Preview.SetFrame(frame);
                Timeline.SetCurrentTime(_currentTime);
            }
        }

        // =========================
        // DRAG & DROP (FIX FOLDERS)
        // =========================

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] items)
            {
                var files = new List<string>();

                foreach (var item in items)
                {
                    if (File.Exists(item))
                        files.Add(item);

                    if (Directory.Exists(item))
                        files.AddRange(
                            Directory.GetFiles(item, "*.*", SearchOption.AllDirectories));
                }

                _playlist = files;
                _index = 0;

                RefreshPlaylist();
                await LoadCurrent();
            }
        }
    }
}