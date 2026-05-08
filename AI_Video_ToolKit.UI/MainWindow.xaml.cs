#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
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

            // Таймлайн
            Timeline.OnChanged += async t =>
            {
                _viewModel.CurrentPosition = t;
                if (_viewModel.CurrentFile != null)
                {
                    var frame = await _playback.GrabCurrentFrame();
                    if (frame != null) Preview.SetFrame(frame);
                }
            };

            // Подписка на кадры из плеера
            _playback.OnFrameChanged += frame => Dispatcher.Invoke(() => Preview.SetFrame(frame));
        }

        // Drag & Drop: Плейлист
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
            e.Handled = true;
        }

        private void Playlist_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        // Drag & Drop: Окно
        private async void Window_Drop(object sender, DragEventArgs e)
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
                await _viewModel.LoadFile(_viewModel.PlaylistItems[0].FilePath);
            e.Handled = true;
        }

        // Drag & Drop: Плеер
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

        // Drag & Drop: Монтажный стол (пока просто добавляем в плейлист, можно доработать)
        private void MontageTable_Drop(object sender, DragEventArgs e)
        {
            // Заглушка, чтобы не сломать компиляцию
            e.Handled = true;
        }

        private void MontageTable_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void MontageList_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
        }

        private void MontageList_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        // Пустые обработчики для сохранения обратной совместимости
        private void LoadMultiple_Click(object sender, RoutedEventArgs e) { }
        private void TogglePlayPause_Click(object sender, RoutedEventArgs e) { }
        private void Stop_Click(object sender, RoutedEventArgs e) { }
        private void Next_Click(object sender, RoutedEventArgs e) { }
        private void Previous_Click(object sender, RoutedEventArgs e) { }
        private void PreviewSegment_Click(object sender, RoutedEventArgs e) { }
        private void ExportSelected_Click(object sender, RoutedEventArgs e) { }
        private void Cut_Click(object sender, RoutedEventArgs e) { }
        private void MergeSelected_Click(object sender, RoutedEventArgs e) { }
        private void UndoMarker_Click(object sender, RoutedEventArgs e) { }
        private void ClearCuts_Click(object sender, RoutedEventArgs e) { }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e) { }

        private static bool IsSupported(string ext) =>
            ext is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm"
                or ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif";
    }
}