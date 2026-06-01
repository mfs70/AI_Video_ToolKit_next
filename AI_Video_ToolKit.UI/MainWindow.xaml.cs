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
using System.Windows.Threading;
using AI_Video_ToolKit.UI.Controls;
using AI_Video_ToolKit.UI.ViewModels;
using AI_Video_ToolKit.UI.Services;
using AI_Video_ToolKit.UI.Messages;
using CommunityToolkit.Mvvm.Messaging;

namespace AI_Video_ToolKit.UI
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly PlaybackService _playback;
        private readonly DispatcherTimer _timelineRefreshTimer;
        private readonly IMessenger _messenger;
        private DateTime _lastTimelinePositionLog = DateTime.MinValue;
        private DateTime _lastTimelineSeekLog = DateTime.MinValue;
        public MainWindow(MainViewModel viewModel, PlaybackService playback, IMessenger messenger)
//       public MainWindow(MainViewModel viewModel, PlaybackService playback)
        {
            InitializeComponent();
            DataContext = viewModel;
            _viewModel = viewModel;
            _playback = playback;
            
            _messenger = messenger;
 // Подписка на тестовое сообщение
            _messenger.Register<PingMessage>(this, (r, m) =>
            {
                Log($"Messenger test: {m.Text}");
            });
            _messenger.Register<LogMessage>(this, (_, m) => Dispatcher.BeginInvoke(() => Log(m.Text)));
            _messenger.Register<ExportFinishedMessage>(this, (_, m) => Dispatcher.BeginInvoke(() =>
            {
                Log(m.Success
                    ? $"Export finished. Result: {m.ResultPath}"
                    : "Export finished with errors.");
            }));


            _playback.OnLog += message => Dispatcher.BeginInvoke(() => Log(message));
            _timelineRefreshTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _timelineRefreshTimer.Tick += (_, _) =>
            {
                if (_playback.IsPlaying)
                    UpdatePositionUi(_playback.CurrentPosition, updatePlaybackService: false);
            };

            double[] speeds = { 0.1, 0.25, 0.5, 1, 2, 4, 8, 16 };
            SpeedCombo.Items.Clear();
            foreach (var s in speeds) SpeedCombo.Items.Add($"{s}x");
            SpeedCombo.SelectedIndex = 3;

            _viewModel.MarkersChanged += UpdateTimelineMarkers;
            _viewModel.ImageLoaded += image => Dispatcher.BeginInvoke(() =>
            {
                Preview.SetImage(image);
                SetPausedState("🖼 Image loaded");
            });
            Timeline.MarkerMoved += (type, original, moved) =>
            {
                _viewModel.MoveTimelineMarker(type.ToString(), original, moved);
                UpdateTimelineMarkers();
            };

            Timeline.OnChanged += async t =>
            {
                await SeekToAsync(t);
                SetPausedState();
            };

            _playback.OnFrameChanged += frame =>
            {
                if (Dispatcher.CheckAccess()) Preview.SetFrame(frame);
                else Dispatcher.BeginInvoke(() => Preview.SetFrame(frame));
            };
            _playback.OnPositionChanged += pos => Dispatcher.BeginInvoke(() =>
            {
                // Playback events are raised from background decoding tasks; all WPF controls
                // and bound view-model properties must be updated on the UI thread.
                UpdatePositionUi(pos, updatePlaybackService: false);
            });
            _playback.OnPlaybackEnded += () => Dispatcher.BeginInvoke(() =>
            {
                SetPausedState();
                Log("Playback ended.");
            });
            Log("Application initialized.");
    // Отправим тестовое сообщение при старте
            _messenger.Send(new Messages.PingMessage("Fedor MainWindow initialized"));
        }

        private void Log(string text)
        {
            LogList.Items.Add($"[{DateTime.Now:HH:mm:ss}] {text}");
            if (LogList.Items.Count > 500)
                LogList.Items.RemoveAt(0);

            LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
        }

        private void UpdatePositionUi(TimeSpan position, bool updatePlaybackService)
        {
            _viewModel.UpdatePosition(position);
            if (updatePlaybackService)
                _playback.SetPosition(position);

            Timeline.SetCurrentTime(position);
            Timeline.SetFrameInfo(_viewModel.CurrentFrame, _viewModel.TotalFrames);
            FrameCountText.Text = $"{_viewModel.CurrentFrame}/{_viewModel.TotalFrames} frames";

            if ((DateTime.Now - _lastTimelinePositionLog).TotalSeconds >= 1)
            {
                _lastTimelinePositionLog = DateTime.Now;
                Log($"Timeline position: {position:hh\\:mm\\:ss\\.fff}, frame {_viewModel.CurrentFrame}/{_viewModel.TotalFrames}, duration {_viewModel.Duration}");
            }
        }

        private async Task SeekToAsync(TimeSpan position)
        {
            _playback.Stop(resetPosition: false);
            UpdatePositionUi(position, updatePlaybackService: true);
            if ((DateTime.Now - _lastTimelineSeekLog).TotalMilliseconds >= 250)
            {
                _lastTimelineSeekLog = DateTime.Now;
                Log($"Timeline seek requested: {position:hh\\:mm\\:ss\\.fff}");
            }
            if (string.IsNullOrEmpty(_viewModel.CurrentFile)) return;

            // Scrubbing changes the authoritative playback position. The preview frame
            // is grabbed after updating PlaybackService so the next Play starts here.
            var frame = await _playback.GrabCurrentFrame();
            if (frame != null) Preview.SetFrame(frame);
        }

        private void UpdateTimelineMarkers()
        {
            var data = _viewModel.GetTimelineData();
            Timeline.SetFrameRate(_viewModel.FileFps);
            Timeline.SetDuration(data.duration);
            Timeline.SetMarkers(data.input, data.output, data.cuts);
            Log($"Timeline configured: duration={data.duration:0.###}s, fps={_viewModel.FileFps:0.###}, cuts={data.cuts.Count}");
        }

        // ==================== Кнопки транспорта ====================
            private void LoadMultiple_Click(object sender, RoutedEventArgs e)
            {
                _viewModel.AddFilesCommand.Execute(null);
            }

//        private async void LoadMultiple_Click(object sender, RoutedEventArgs e)
//        {
//            var dlg = new Microsoft.Win32.OpenFileDialog
//            {
//                Filter = "Media files|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.jpg;*.jpeg;*.png;*.bmp;*.gif",
//                Multiselect = true
//            };
//            if (dlg.ShowDialog() == true)
//            {
//                foreach (var path in dlg.FileNames)
//                    _viewModel.AddToPlaylist(path);
//                if (_viewModel.PlaylistItems.Count > 0 && string.IsNullOrEmpty(_viewModel.CurrentFile))
//                {
//                    await _viewModel.LoadFile(_viewModel.PlaylistItems[0].FilePath);
//                    ScrollSelectedPlaylistItemIntoView();
//                    SyncPlaybackStateAfterLoad();
//                }
//            }
//        }

        private async void TogglePlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_viewModel.CurrentFile))
            {
                if (_viewModel.PlaylistItems.Count > 0)
                {
                    await _viewModel.LoadFile(_viewModel.PlaylistItems[0].FilePath);
                    ScrollSelectedPlaylistItemIntoView();
                    SyncPlaybackStateAfterLoad();
                }
                return;
            }

            if (_playback.IsPlaying)
            {
                _playback.Pause();
                SetPausedState();
                Log("Playback paused.");
            }
            else
            {
                _playback.Resume();
                if (_playback.IsPlaying)
                    SetPlayingState("▶ Playing");
            }
        }

        private async void Stop_Click(object sender, RoutedEventArgs e)
        {
            _playback.Stop();
            _playback.SetPosition(TimeSpan.Zero);
            SetStoppedState();
            _viewModel.UpdatePosition(TimeSpan.Zero);
            var frame = await _playback.GrabCurrentFrame();
            if (frame != null) Preview.SetFrame(frame);
            Timeline.SetCurrentTime(TimeSpan.Zero);
            Log("Playback stopped and reset to start.");
        }

		private void Previous_Click(object sender, RoutedEventArgs e)
		{
			_viewModel.Previous();
		}
		private void Next_Click(object sender, RoutedEventArgs e)
		{
			_viewModel.Next();
		}
        /* private void Previous_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.PlaylistItems.Count == 0) return;
            int idx = _viewModel.PlaylistItems.IndexOf(_viewModel.SelectedPlaylistItem!);
            if (idx <= 0) idx = _viewModel.PlaylistItems.Count - 1;
            else idx--;
            _ = LoadAndSync(_viewModel.PlaylistItems[idx].FilePath);
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.PlaylistItems.Count == 0) return;
            int idx = _viewModel.PlaylistItems.IndexOf(_viewModel.SelectedPlaylistItem!);
            if (idx < 0 || idx >= _viewModel.PlaylistItems.Count - 1) idx = 0;
            else idx++;
            _ = LoadAndSync(_viewModel.PlaylistItems[idx].FilePath);
        } */

        private async void PlaylistListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (PlaylistListBox.SelectedItem is not PlaylistItem item) return;
            await LoadAndSync(item.FilePath);
            Log($"Playlist item opened: {item.FileName}");
        }

        // ==================== Drag & Drop ====================
        private void Playlist_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            e.Handled = true;
            var items = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (items == null) return;
            AddDroppedFiles(items);
// Загрузка первого файла теперь происходит внутри PlaylistViewModel, если плейлист был пуст
//           var firstAdded = AddDroppedFiles(items);
//            if (_viewModel.PlaylistItems.Count > 0 && string.IsNullOrEmpty(_viewModel.CurrentFile))
//                _ = LoadAndSync(firstAdded ?? _viewModel.PlaylistItems[0].FilePath);
        }

        private void Playlist_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Handled) return;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            e.Handled = true;
            var items = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (items == null) return;
            AddDroppedFiles(items);
// Загрузка первого файла теперь происходит внутри PlaylistViewModel, если плейлист был пуст
//            var firstAdded = AddDroppedFiles(items);
//            if (_viewModel.PlaylistItems.Count > 0 && string.IsNullOrEmpty(_viewModel.CurrentFile))
//            {
//                await _viewModel.LoadFile(firstAdded ?? _viewModel.PlaylistItems[0].FilePath);
//                ScrollSelectedPlaylistItemIntoView();
//                SyncPlaybackStateAfterLoad();
//            }
        }

        private void Player_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            e.Handled = true;
            var items = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (items == null) return;
            AddDroppedFiles(items);
// Загрузка первого файла теперь происходит внутри PlaylistViewModel, если плейлист был пуст
//            var firstAdded = AddDroppedFiles(items);
//            if (firstAdded != null)
//            {
//                await _viewModel.LoadFile(firstAdded);
//                ScrollSelectedPlaylistItemIntoView();
//                SyncPlaybackStateAfterLoad();
//            }
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

        private async void MergeSelectedMontage_Click(object sender, RoutedEventArgs e)
        {
            var selected = MontageList.SelectedItems
                .OfType<MontageItem>()
                .ToList();
            Log($"Action clicked: merge selected ({selected.Count}).");
            await _viewModel.MergeSelectedMontageItems(selected);
        }

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
            if (e.Key == Key.Delete)
            {
                _viewModel.RemoveSelectedFromPlaylistCommand.Execute(null);
                ScrollSelectedPlaylistItemIntoView();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.R) { e.Handled = true; return; }
            if (e.Key == Key.A) { e.Handled = true; return; }
            if (e.Key == Key.V) { e.Handled = true; return; }
            if (e.Key == Key.M) { e.Handled = true; return; }
            if (e.Key == Key.Right || e.Key == Key.Left)
            {
                // Window preview keys are raised before TimelineControl gets the event.
                // Give a selected timeline marker priority; otherwise arrows step playback.
                if (Timeline.TryMoveSelectedMarkerByKey(e.Key, Keyboard.Modifiers))
                {
                    e.Handled = true;
                    return;
                }

                if (Timeline.IsPlayheadSelected && Timeline.TryMovePlayheadByKey(e.Key, Keyboard.Modifiers))
                {
                    e.Handled = true;
                    return;
                }

                var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
                Step(e.Key == Key.Right ? step : -step);
                e.Handled = true;
                return;
            }
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
            var targetFrame = Math.Clamp(_viewModel.CurrentFrame + frames, 0, Math.Max(0, _viewModel.TotalFrames - 1));
            var fps = _viewModel.FileFps > 0 ? _viewModel.FileFps : 25;
            await SeekToAsync(TimeSpan.FromSeconds(targetFrame / fps));
            SetPausedState();
        }

        private static bool IsSupported(string ext) =>
            ext is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm"
                or ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif";

        private string AddDroppedFiles(IEnumerable<string> items)
        {
            string firstAdded = null;
            var addedCount = 0;

            foreach (var path in items)
            {
                if (Directory.Exists(path))
                {
                    foreach (var file in Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                        .Where(f => IsSupported(Path.GetExtension(f))))
                    {
                        if (_viewModel.AddToPlaylist(file))
                        {
                            firstAdded ??= file;
                            addedCount++;
                        }
                    }
                }
                else if (File.Exists(path) && _viewModel.AddToPlaylist(path))
                {
                    firstAdded ??= path;
                    addedCount++;
                }
            }

            Log(addedCount > 0 ? $"Added files by drag/drop: {addedCount}" : "Drag/drop skipped: files already in playlist or unsupported.");
            return firstAdded;
        }


        // ------------------------------------------------------------
        // Save logger contents to UTF-8 text file.
        // ------------------------------------------------------------
        private void SaveLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (LogList.Items.Count == 0)
                {
                    MessageBox.Show(
                        "Log is empty.",
                        "Save Log",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return;
                }

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save Log",
                    Filter =
                        "Log files (*.log)|*.log|" +
                        "Text files (*.txt)|*.txt",

                    DefaultExt = ".log",

                    FileName =
                        $"AI_Video_ToolKit_Log_" +
                        $"{DateTime.Now:yyyyMMdd_HHmmss}.log"
                };

                if (dialog.ShowDialog() != true)
                    return;

                var lines = LogList.Items
                    .Cast<object>()
                    .Select(x => x?.ToString() ?? string.Empty)
                    .ToArray();

                File.WriteAllLines(
                    dialog.FileName,
                    lines,
                    System.Text.Encoding.UTF8);

                Log($"Log saved: {dialog.FileName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to save log:\n{ex.Message}",
                    "Save Log Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SetPlayingState(string status)
        {
            _viewModel.IsPlaying = true;
            _viewModel.StatusText = status;
            PlayIcon.Text = "⏸";
            PlayIcon.Foreground = Brushes.Yellow;
            if (!_timelineRefreshTimer.IsEnabled)
            {
                _timelineRefreshTimer.Start();
                Log("Timeline refresh timer started.");
            }
        }

        private async Task LoadAndSync(string filePath)
        {
            await _viewModel.LoadFile(filePath);
            ScrollSelectedPlaylistItemIntoView();
            SyncPlaybackStateAfterLoad();
        }

        private void ScrollSelectedPlaylistItemIntoView()
        {
            if (_viewModel.SelectedPlaylistItem == null) return;

            // The playlist is virtualized, so scrolling must target the bound item,
            // not a visual ListBoxItem that may not exist yet.
            PlaylistListBox.ScrollIntoView(_viewModel.SelectedPlaylistItem);
        }

        private void SyncPlaybackStateAfterLoad()
        {
            if (_playback.IsPlaying)
                SetPlayingState("▶ Playing");
        }

        private void SetPausedState(string status = "⏸ Paused")
        {
            _viewModel.IsPlaying = false;
            _viewModel.StatusText = status;
            PlayIcon.Text = "▶";
            PlayIcon.Foreground = Brushes.White;
            if (_timelineRefreshTimer.IsEnabled)
            {
                _timelineRefreshTimer.Stop();
                Log("Timeline refresh timer stopped.");
            }
        }

        private void SetStoppedState()
        {
            _viewModel.IsPlaying = false;
            _viewModel.StatusText = "⏹ Stopped";
            PlayIcon.Text = "▶";
            PlayIcon.Foreground = Brushes.White;
            if (_timelineRefreshTimer.IsEnabled)
            {
                _timelineRefreshTimer.Stop();
                Log("Timeline refresh timer stopped.");
            }
        }
    }
}
