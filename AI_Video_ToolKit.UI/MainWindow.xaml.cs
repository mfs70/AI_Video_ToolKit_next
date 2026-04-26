using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Microsoft.Win32;

using AI_Video_ToolKit.Core;
using AI_Video_ToolKit.Infrastructure;

namespace AI_Video_ToolKit.UI
{
    public partial class MainWindow : Window
    {
        private VideoEditService _edit;

        private List<string> _files = new();
        private int _currentIndex = -1;

        private System.Windows.Shapes.Rectangle? cropRect;
        private System.Windows.Point startPoint;

        public MainWindow()
        {
            InitializeComponent();

            var ffmpeg = new FFmpegService(@"C:\_Portable_\ffmpeg\bin\ffmpeg.exe");
            _edit = new VideoEditService(ffmpeg);

            InitCropTool();
        }

        // ================= FILE LOAD =================

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

        private bool IsSupported(string f)
        {
            string ext = System.IO.Path.GetExtension(f).ToLower();
            return ext == ".mp4" || ext == ".mkv" || ext == ".png" || ext == ".jpg";
        }

        private void OpenFiles_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Multiselect = true };

            if (dialog.ShowDialog() == true)
                LoadFiles(dialog.FileNames);
        }

        private void LoadCurrent()
        {
            if (_currentIndex < 0 || _currentIndex >= _files.Count) return;

            string file = _files[_currentIndex];

            CurrentFileText.Text = file;

            if (IsVideo(file))
            {
                VideoPlayer.Source = new Uri(file);
                VideoPlayer.Play();
            }
        }

        // ================= PLAYER =================

        private void Play_Click(object sender, RoutedEventArgs e) => VideoPlayer.Play();
        private void Pause_Click(object sender, RoutedEventArgs e) => VideoPlayer.Pause();
        private void Stop_Click(object sender, RoutedEventArgs e) => VideoPlayer.Stop();

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

        // ================= HELPERS =================

        private string? GetCurrentFile()
        {
            if (_currentIndex < 0) return null;
            return _files[_currentIndex];
        }

        private string GetOutput(string name)
        {
            string dir = @"D:\output";
            System.IO.Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, name);
        }

        // ================= OPERATIONS =================

        private void Trim_Click(object sender, RoutedEventArgs e)
        {
            var input = GetCurrentFile();
            if (input == null) return;

            var result = _edit.Trim(input, GetOutput("trim.mp4"), "00:00:02", "00:00:06");

            LogBox.Text = result.Item2;
            System.Windows.MessageBox.Show(result.Item1 ? "OK" : "ERROR");
        }

        private void Split_Click(object sender, RoutedEventArgs e)
        {
            var input = GetCurrentFile();
            if (input == null) return;

            var result = _edit.Split(input, GetOutput("part_%03d.mp4"), 5);

            LogBox.Text = result.Item2;
            System.Windows.MessageBox.Show(result.Item1 ? "OK" : "ERROR");
        }

        private void Crop_Click(object sender, RoutedEventArgs e)
        {
            if (cropRect == null) return;

            var input = GetCurrentFile();
            if (input == null) return;

            double x = Canvas.GetLeft(cropRect);
            double y = Canvas.GetTop(cropRect);

            double w = cropRect.Width;
            double h = cropRect.Height;

            var result = _edit.Crop(input, GetOutput("crop.mp4"),
                (int)w, (int)h, (int)x, (int)y);

            LogBox.Text = result.Item2;
            System.Windows.MessageBox.Show(result.Item1 ? "OK" : "ERROR");
        }

        // ================= CROP =================

        private void InitCropTool()
        {
            cropRect = new System.Windows.Shapes.Rectangle
            {
                Stroke = System.Windows.Media.Brushes.Red,
                StrokeThickness = 2,
                Width = 200,
                Height = 150
            };

            CropCanvas.Children.Add(cropRect);

            CropCanvas.MouseLeftButtonDown += (s, e) =>
            {
                startPoint = e.GetPosition(CropCanvas);
                Canvas.SetLeft(cropRect, startPoint.X);
                Canvas.SetTop(cropRect, startPoint.Y);
            };

            CropCanvas.MouseMove += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    var pos = e.GetPosition(CropCanvas);

                    cropRect.Width = Math.Abs(pos.X - startPoint.X);
                    cropRect.Height = Math.Abs(pos.Y - startPoint.Y);
                }
            };
        }

        // ================= DRAG & DROP =================

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;

            var items = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            LoadFiles(items);
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
        }

        private bool IsVideo(string f)
        {
            string ext = System.IO.Path.GetExtension(f).ToLower();
            return ext == ".mp4" || ext == ".mkv";
        }
    }
}