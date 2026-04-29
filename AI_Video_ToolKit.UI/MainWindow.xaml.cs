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

        private bool _isPlaying = false;
        private bool _userInteracting = false;

        private CancellationTokenSource? _seekCts;

        public MainWindow()
        {
            InitializeComponent();

            // ==== VIDEO OUTPUT ====
            _player.OnFrame += frame =>
                Dispatcher.Invoke(() => Preview.SetFrame(frame));

            // ==== TIME SYNC ====
            _player.OnPositionChanged += time =>
            {
                _currentTime = time;

                if (_userInteracting) return;

                Dispatcher.Invoke(() =>
                    Timeline.SetCurrentTime(time));
            };

            // ==== LOOP / PLAYLIST ====
            _player.OnPlaybackEnded += () =>
            {
                Dispatcher.Invoke(async () =>
                {
                    // LOOP текущего файла
                    if (LoopCheck.IsChecked == true)
                    {
                        RestartPlayback();
                        return;
                    }

                    // следующий файл
                    if (_index < _playlist.Count - 1)
                    {
                        _index++;
                        RefreshPlaylist();
                        await LoadCurrent();
                    }
                    else
                    {
                        _isPlaying = false;
                    }
                });
            };

            // ==== TIMELINE ====
            Timeline.OnUserInteraction += active =>
                _userInteracting = active;

            Timeline.OnTimeChanged += Timeline_OnTimeChanged;
        }

        // ================= LOAD =================

        private async void Load_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Multiselect = true
            };

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

            Timeline.SetDuration(info.duration);
            Timeline.SetCurrentTime(TimeSpan.Zero);

            // первый кадр
            var frame = await _grabber.GetFrame(file, _currentTime, 1280, 720);
            if (frame != null)
                Preview.SetFrame(frame);

            StartPlayback();
        }

        private void RefreshPlaylist()
        {
            PlaylistBox.ItemsSource = null;
            PlaylistBox.ItemsSource = _playlist.Select(Path.GetFileName);
            PlaylistBox.SelectedIndex = _index;
        }

        // ================= PLAY =================

        private void StartPlayback()
        {
            if (_index < 0) return;

            _player.Start(_playlist[_index], 1280, 720, _fps, _currentTime);
            _isPlaying = true;
        }

        private void RestartPlayback()
        {
            if (_index < 0) return;

            _player.Stop();
            _player.Start(_playlist[_index], 1280, 720, _fps, _currentTime);
            _isPlaying = true;
        }

        private void TogglePlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_index < 0) return;

            if (_isPlaying)
            {
                _player.Pause();
                _isPlaying = false;
            }
            else
            {
                _player.Resume();
                _isPlaying = true;
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            _player.Stop();
            _isPlaying = false;

            _currentTime = TimeSpan.Zero;
            Timeline.SetCurrentTime(_currentTime);
        }

        // ================= NAV =================

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

        // ================= TIMELINE =================

        private void Timeline_OnTimeChanged(TimeSpan time)
        {
            _currentTime = time;

            _seekCts?.Cancel();
            _seekCts = new CancellationTokenSource();

            var token = _seekCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(120, token);

                    if (token.IsCancellationRequested) return;

                    Dispatcher.Invoke(() =>
                    {
                        RestartPlayback();
                    });
                }
                catch { }
            });
        }

        // ================= HOTKEYS =================

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
        }

        private async void StepFrame(int direction)
        {
            if (_fps <= 0 || _index < 0) return;

            double frameTime = 1.0 / _fps;

            _currentTime += TimeSpan.FromSeconds(direction * frameTime);

            if (_currentTime < TimeSpan.Zero)
                _currentTime = TimeSpan.Zero;

            _player.Pause();
            _isPlaying = false;

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

        // ================= DRAG & DROP =================

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