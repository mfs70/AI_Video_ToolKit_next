using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;

using IOPath = System.IO.Path;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Imaging;

using Microsoft.Win32;

using AI_Video_ToolKit.Core;
using AI_Video_ToolKit.Infrastructure;
using AI_Video_ToolKit.Domain;

namespace AI_Video_ToolKit.UI
{
    public partial class MainWindow : Window
    {
        private FFmpegService _ffmpeg;
        private VideoEditService _edit;

        private List<string> _files = new();
        private int _currentIndex = -1;

        private TimelineService _timeline = new();

        private Dictionary<string, BitmapImage> _thumbCache = new();

        private bool _isDragging;
        private bool _isResizing;
        private Point _startMouse;
        private double _startX;
        private double _startWidth;

        public MainWindow()
        {
            InitializeComponent();

            string ffmpegPath = @"C:\_Portable_\ffmpeg\bin\ffmpeg.exe";

            _ffmpeg = new FFmpegService(ffmpegPath);
            _edit = new VideoEditService(_ffmpeg);
        }

        // ================= FILE LOAD =================

        private void OpenFiles_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Multiselect = true };

            if (dialog.ShowDialog() == true)
                LoadFiles(dialog.FileNames);
        }

        private void LoadFiles(IEnumerable<string> items)
        {
            foreach (var path in items)
            {
                if (Directory.Exists(path))
                {
                    var files = Directory.GetFiles(path);
                    foreach (var f in files)
                        AddFile(f);
                }
                else if (File.Exists(path))
                {
                    AddFile(path);
                }
            }

            if (_files.Count > 0)
            {
                _currentIndex = 0;
                LoadCurrent();
            }

            RedrawTimeline();
        }

        private void AddFile(string file)
        {
            if (!IsSupported(file)) return;

            _files.Add(file);
            _timeline.Add(file);
        }

        private bool IsSupported(string f)
        {
            string ext = IOPath.GetExtension(f).ToLower();
            return ext == ".mp4" || ext == ".mkv" || ext == ".png" || ext == ".jpg";
        }

        // ================= PREVIEW =================

        private void LoadCurrent()
        {
            if (_currentIndex < 0 || _currentIndex >= _files.Count) return;

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

        private bool IsVideo(string f)
        {
            string ext = IOPath.GetExtension(f).ToLower();
            return ext == ".mp4" || ext == ".mkv";
        }

        // ================= THUMBNAILS =================

        private BitmapImage GetThumbnail(string file)
        {
            if (_thumbCache.ContainsKey(file))
                return _thumbCache[file];

            string thumbPath = IOPath.Combine(IOPath.GetTempPath(), IOPath.GetFileName(file) + ".jpg");

            if (!File.Exists(thumbPath))
            {
                if (IsVideo(file))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = @"C:\_Portable_\ffmpeg\bin\ffmpeg.exe",
                        Arguments = $"-y -i \"{file}\" -ss 00:00:01 -vframes 1 \"{thumbPath}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };

                    Process.Start(psi)?.WaitForExit();
                }
                else
                {
                    File.Copy(file, thumbPath, true);
                }
            }

            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(thumbPath);
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();

            _thumbCache[file] = img;
            return img;
        }

        // ================= TIMELINE =================

        private void RedrawTimeline()
        {
            TimelineCanvas.Children.Clear();

            foreach (var item in _timeline.Items)
            {
                var container = new Grid
                {
                    Width = item.Width,
                    Height = 80,
                    Tag = item
                };

                var bg = new Border
                {
                    Background = Brushes.Black,
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(1)
                };

                var img = new Image
                {
                    Source = GetThumbnail(item.FilePath),
                    Stretch = Stretch.Fill
                };

                container.Children.Add(bg);
                container.Children.Add(img);

                Canvas.SetLeft(container, item.X);
                Canvas.SetTop(container, 40);

                container.MouseLeftButtonDown += Clip_MouseDown;
                container.MouseMove += Clip_MouseMove;
                container.MouseLeftButtonUp += Clip_MouseUp;

                TimelineCanvas.Children.Add(container);
            }
        }

        private void Clip_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element == null) return;

            _startMouse = e.GetPosition(TimelineCanvas);
            _startX = Canvas.GetLeft(element);
            _startWidth = element.Width;

            if (_startMouse.X > _startX + element.Width - 10)
                _isResizing = true;
            else
                _isDragging = true;

            element.CaptureMouse();
        }

        private void Clip_MouseMove(object sender, MouseEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element == null) return;

            var pos = e.GetPosition(TimelineCanvas);
            double dx = pos.X - _startMouse.X;

            var item = (TimelineItem)element.Tag;

            if (_isDragging)
            {
                double newX = _startX + dx;
                if (newX < 0) newX = 0;

                Canvas.SetLeft(element, newX);
                item.X = newX;
            }

            if (_isResizing)
            {
                double newWidth = _startWidth + dx;
                if (newWidth < 40) newWidth = 40;

                element.Width = newWidth;
                item.Width = newWidth;
            }
        }

        private void Clip_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            _isResizing = false;

            (sender as FrameworkElement)?.ReleaseMouseCapture();
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

        // ================= FFMPEG ACTIONS (FIX ОШИБКИ) =================

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

        // ================= HELPERS =================

        private string GetOutput(string name)
        {
            string dir = @"D:\output";
            Directory.CreateDirectory(dir);
            return IOPath.Combine(dir, name);
        }

        private string? GetCurrentFile()
        {
            if (_currentIndex < 0) return null;
            return _files[_currentIndex];
        }

        // ================= DRAG DROP =================

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var items = (string[])e.Data.GetData(DataFormats.FileDrop);
            LoadFiles(items);
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Copy;
        }
    }
}