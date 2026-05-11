#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AI_Video_ToolKit.UI.Controls;
using AI_Video_ToolKit.UI.ViewModels;
using AI_Video_ToolKit.UI.Services;

namespace AI_Video_ToolKit.UI
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly PlaybackService _playback;

        public MainWindow(MainViewModel viewModel, PlaybackService playback)
        {
            InitializeComponent();
            DataContext = viewModel;
            _viewModel = viewModel;
            _playback = playback;

            double[] speeds = { 0.1, 0.25, 0.5, 1, 2, 4, 8, 16 };
            SpeedCombo.Items.Clear();
            foreach (var s in speeds) SpeedCombo.Items.Add($"{s}x");
            SpeedCombo.SelectedIndex = 3;

            _viewModel.MarkersChanged += UpdateTimelineMarkers;

            Timeline.OnChanged += async t =>
            {
                _viewModel.UpdatePosition(t);
                var frame = await _playback.GrabCurrentFrame();
                if (frame != null) Preview.SetFrame(frame);
            };

            _playback.OnFrameChanged += frame => Dispatcher.Invoke(() => Preview.SetFrame(frame));
            _playback.OnPositionChanged += pos =>
            {
                Timeline.SetCurrentTime(pos);
                Timeline.SetFrameInfo(_viewModel.CurrentFrame, _viewModel.TotalFrames);
            };
            _playback.OnPlaybackEnded += () => Dispatcher.Invoke(() =>
            {
                _viewModel.IsPlaying = false;
                _viewModel.StatusText = "⏸ Paused";
                PlayIcon.Text = "▶";
                PlayIcon.Foreground = Brushes.White;
            });
        }

        private void UpdateTimelineMarkers()
        {
            var data = _viewModel.GetTimelineData();
            Timeline.SetDuration(data.duration);
            Timeline.SetMarkers(data.input, data.output, data.cuts);
        }

        // ==================== Кнопки транспорта ====================
        private async void LoadMultiple_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Media files|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.jpg;*.jpeg;*.png;*.bmp;*.gif",
                Multiselect = true
            };
            if (dlg.ShowDialog() == true)
            {
                foreach (var path in dlg.FileNames)
                    _viewModel.AddToPlaylist(path);
                if (_viewModel.PlaylistItems.Count > 0 && string.IsNullOrEmpty(_viewModel.CurrentFile))
                    await _viewModel.LoadFile(_viewModel.PlaylistItems[0].FilePath);
            }
        }

        private async void TogglePlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_viewModel.CurrentFile))
            {
                if (_viewModel.PlaylistItems.Count > 0)
                    await _viewModel.LoadFile(_viewModel.PlaylistItems[0].FilePath);
                return;
            }

            if (_playback.IsPlaying)
            {
                _playback.Pause();
                _viewModel.IsPlaying = false;
                _viewModel.StatusText = "⏸ Paused";
                PlayIcon.Text = "▶";
                PlayIcon.Foreground = Brushes.White;
            }
            else
            {
                _playback.Resume();
                _viewModel.IsPlaying = true;
                _viewModel.StatusText = "▶ Playing";
                PlayIcon.Text = "⏸";
                PlayIcon.Foreground = Brushes.Yellow;
            }
        }

        private async void Stop_Click(object sender, RoutedEventArgs e)
        {
            _playback.Stop();
            _viewModel.IsPlaying = false;
            _viewModel.StatusText = "⏹ Stopped";
            _viewModel.UpdatePosition(TimeSpan.Zero);
            var frame = await _playback.GrabCurrentFrame();
            if (frame != null) Preview.SetFrame(frame);
            PlayIcon.Text = "▶";
            PlayIcon.Foreground = Brushes.White;
            Timeline.SetCurrentTime(TimeSpan.Zero);
        }

        private void Previous_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.PlaylistItems.Count == 0) return;
            int idx = _viewModel.PlaylistItems.IndexOf(_viewModel.SelectedPlaylistItem!);
            if (idx <= 0) idx = _viewModel.PlaylistItems.Count - 1;
            else idx--;
            _ = _viewModel.LoadFile(_viewModel.PlaylistItems[idx].FilePath);
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.PlaylistItems.Count == 0) return;
            int idx = _viewModel.PlaylistItems.IndexOf(_viewModel.SelectedPlaylistItem!);
            if (idx < 0 || idx >= _viewModel.PlaylistItems.Count - 1) idx = 0;
            else idx++;
            _ = _viewModel.LoadFile(_viewModel.PlaylistItems[idx].FilePath);
        }

        // ==================== Drag & Drop ====================
        private void Playlist_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var items = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (items == null) return;
            foreach (var path in items)
            {
                if (Directory.Exists(path))
                {
                    foreach (var file in Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                        .Where(f => IsSupported(Path.GetExtension(f))))
                        _viewModel.AddToPlaylist(file);
                }
                else if (File.Exists(path))
                    _viewModel.AddToPlaylist(path);
            }
            if (_viewModel.PlaylistItems.Count > 0 && string.IsNullOrEmpty(_viewModel.CurrentFile))
                _ = _viewModel.LoadFile(_viewModel.PlaylistItems[0].FilePath);
            e.Handled = true;
        }

        private void Playlist_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Handled) return;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var items = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (items == null) return;
            foreach (var path in items)
            {
                if (Directory.Exists(path))
                {
                    foreach (var file in Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                        .Where(f => IsSupported(Path.GetExtension(f))))
                        _viewModel.AddToPlaylist(file);
                }
                else if (File.Exists(path))
                    _viewModel.AddToPlaylist(path);
            }
            if (_viewModel.PlaylistItems.Count > 0 && string.IsNullOrEmpty(_viewModel.CurrentFile))
                await _viewModel.LoadFile(_viewModel.PlaylistItems[0].FilePath);
            e.Handled = true;
        }

        private async void Player_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var items = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (items == null) return;
            foreach (var path in items)
            {
                if (Directory.Exists(path))
                {
                    foreach (var file in Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                        .Where(f => IsSupported(Path.GetExtension(f))))
                        _viewModel.AddToPlaylist(file);
                }
                else if (File.Exists(path))
                    _viewModel.AddToPlaylist(path);
            }
            if (_viewModel.PlaylistItems.Count > 0)
                await _viewModel.LoadFile(_viewModel.PlaylistItems[0].FilePath);
            e.Handled = true;
        }

        private void Player_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void MontageTable_Drop(object sender, DragEventArgs e) { e.Handled = true; }
        private void MontageTable_DragOver(object sender, DragEventArgs e) { e.Effects = DragDropEffects.None; e.Handled = true; }
        private void MontageList_Drop(object sender, DragEventArgs e) { e.Handled = true; }
        private void MontageList_DragOver(object sender, DragEventArgs e) { e.Effects = DragDropEffects.None; e.Handled = true; }

        // ==================== Горячие клавиши ====================
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space) { TogglePlayPause_Click(sender, e); e.Handled = true; return; }
            if (e.Key == Key.K || e.Key == Key.S) { Stop_Click(sender, e); e.Handled = true; return; }
            if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control) { LoadMultiple_Click(sender, e); e.Handled = true; return; }
            if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.None) { IncreaseSpeed(); e.Handled = true; return; }
            if (e.Key == Key.J && Keyboard.Modifiers == ModifierKeys.None) { DecreaseSpeed(); e.Handled = true; return; }
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) { _viewModel.UndoMarkerCommand.Execute(null); e.Handled = true; return; }
            if (e.Key == Key.I) { _viewModel.MarkInputCommand.Execute(null); e.Handled = true; return; }
            if (e.Key == Key.O) { _viewModel.MarkOutputCommand.Execute(null); e.Handled = true; return; }
            if (e.Key == Key.C) { _viewModel.MarkCutCommand.Execute(null); e.Handled = true; return; }
            if (e.Key == Key.Delete) { e.Handled = true; return; }
            if (e.Key == Key.R) { e.Handled = true; return; }
            if (e.Key == Key.A) { e.Handled = true; return; }
            if (e.Key == Key.V) { e.Handled = true; return; }
            if (e.Key == Key.M) { e.Handled = true; return; }
            if (e.Key == Key.Right) { Step(Keyboard.Modifiers == ModifierKeys.Shift ? 10 : 1); e.Handled = true; return; }
            if (e.Key == Key.Left) { Step(Keyboard.Modifiers == ModifierKeys.Shift ? -10 : -1); e.Handled = true; return; }
        }

        private void IncreaseSpeed()
        {
            if (_viewModel.SelectedSpeedIndex < 7) _viewModel.SelectedSpeedIndex++;
        }

        private void DecreaseSpeed()
        {
            if (_viewModel.SelectedSpeedIndex > 0) _viewModel.SelectedSpeedIndex--;
        }

        private async void Step(int frames)
        {
            if (string.IsNullOrEmpty(_viewModel.CurrentFile)) return;
            _playback.Stop();
            _viewModel.CurrentFrame = Math.Clamp(_viewModel.CurrentFrame + frames, 0, _viewModel.TotalFrames);
            _viewModel.CurrentPosition = TimeSpan.FromSeconds(_viewModel.CurrentFrame / (double.TryParse(_viewModel.FpsStr, out var f) ? f : 25));
            var frame = await _playback.GrabCurrentFrame();
            if (frame != null) Preview.SetFrame(frame);
            _viewModel.IsPlaying = false;
            _viewModel.StatusText = "⏸ Paused";
        }

        private static bool IsSupported(string ext) =>
            ext is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm"
                or ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif";
    }
}