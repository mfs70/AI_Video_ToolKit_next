using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

using Microsoft.Win32;

using AI_Video_ToolKit.Core;
using AI_Video_ToolKit.Infrastructure;

namespace AI_Video_ToolKit.UI
{
    public partial class MainWindow : Window
    {
        private VideoEditService _edit;
        private FFmpegService _ffmpeg;

        private List<string> _files = new();
        private int _currentIndex = -1;

        public MainWindow()
        {
            InitializeComponent();

            string ffmpegPath = @"C:\_Portable_\ffmpeg\bin\ffmpeg.exe";

            _ffmpeg = new FFmpegService(ffmpegPath);
            _edit = new VideoEditService(_ffmpeg);
        }

        private void LoadFiles(IEnumerable<string> items)
        {
            _files.Clear();

            foreach (var path in items)
            {
                if (Directory.Exists(path))
                {
                    var files = Directory.GetFiles(path);
                    _files.AddRange(files.Where(IsSupported));
                }
                else if (File.Exists(path))
                {
                    if (IsSupported(path))
                        _files.Add(path);
                }
            }

            if (_files.Count > 0)
            {
                _currentIndex = 0;
                LoadCurrent();
            }
        }

        private void LoadCurrent()
        {
            if (_currentIndex < 0) return;

            string file = _files[_currentIndex];
            CurrentFileText.Text = file;

            if (IsVideo(file))
            {
                ImageViewer.Visibility = Visibility.Collapsed;
                VideoPlayer.Visibility = Visibility.Visible;

                VideoPlayer.Source = new Uri(file);
                VideoPlayer.Play();
            }
            else
            {
                VideoPlayer.Stop();
                VideoPlayer.Visibility = Visibility.Collapsed;

                ImageViewer.Visibility = Visibility.Visible;
                ImageViewer.Source = new BitmapImage(new Uri(file));
            }
        }

        private void OpenFiles_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Multiselect = true };
            if (dialog.ShowDialog() == true)
                LoadFiles(dialog.FileNames);
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            var items = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            LoadFiles(items);
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex < _files.Count - 1)
            {
                _currentIndex++;
                LoadCurrent();
            }
        }

        private void Prev_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex > 0)
            {
                _currentIndex--;
                LoadCurrent();
            }
        }

        private void Play_Click(object sender, RoutedEventArgs e) => VideoPlayer.Play();
        private void Pause_Click(object sender, RoutedEventArgs e) => VideoPlayer.Pause();
        private void Stop_Click(object sender, RoutedEventArgs e) => VideoPlayer.Stop();

        private string? GetCurrentFile()
        {
            if (_currentIndex < 0) return null;
            return _files[_currentIndex];
        }

        private string GetOutput(string name)
        {
            string dir = @"D:\output";
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, name);
        }

        private void Trim_Click(object sender, RoutedEventArgs e)
        {
            var input = GetCurrentFile();
            if (input == null) return;

            var r = _edit.Trim(input, GetOutput("trim.mp4"), "00:00:02", "00:00:05");

            LogBox.Text = r.Item2;
        }

        private void Split_Click(object sender, RoutedEventArgs e)
        {
            var input = GetCurrentFile();
            if (input == null) return;

            var r = _edit.Split(input, GetOutput("part_%03d.mp4"), 5);

            LogBox.Text = r.Item2;
        }

        private void Crop_Click(object sender, RoutedEventArgs e)
        {
            var input = GetCurrentFile();
            if (input == null) return;

            var r = _edit.Crop(input, GetOutput("crop.mp4"), 300, 300, 0, 0);

            LogBox.Text = r.Item2;
        }

        private void Probe_Click(object sender, RoutedEventArgs e)
        {
            var input = GetCurrentFile();
            if (input == null) return;

            var r = _ffmpeg.Probe(input);

            LogBox.Text = r.Item2;
        }

        private bool IsSupported(string f)
        {
            string ext = Path.GetExtension(f).ToLower();
            return ext == ".mp4" || ext == ".mkv" || ext == ".png" || ext == ".jpg";
        }

        private bool IsVideo(string f)
        {
            string ext = Path.GetExtension(f).ToLower();
            return ext == ".mp4" || ext == ".mkv";
        }
    }
}