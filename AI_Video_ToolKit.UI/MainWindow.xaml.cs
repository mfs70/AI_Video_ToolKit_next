using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Windows.Threading;

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

        private DispatcherTimer _timer = new();
        private bool _isSeeking = false;

        public MainWindow()
        {
            InitializeComponent();

            _ffmpeg = new FFmpegService(@"C:\_Portable_\ffmpeg\bin\ffmpeg.exe");
            _edit = new VideoEditService(_ffmpeg);

            _timer.Interval = TimeSpan.FromMilliseconds(200);
            _timer.Tick += UpdateSeekBar;
            _timer.Start();
        }

        // ================= FILES =================

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
                    foreach (var f in Directory.GetFiles(path))
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
            string ext = IOPath.GetExtension(file).ToLower();
            if (ext != ".mp4" && ext != ".mkv" && ext != ".png" && ext != ".jpg") return;

            _files.Add(file);
            _timeline.Add(file);
        }

        // ================= PREVIEW =================

        private void LoadCurrent()
        {
            if (_currentIndex < 0 || _currentIndex >= _files.Count) return;

            string file = _files[_currentIndex];
            CurrentFileText.Text = file;

            if (file.EndsWith(".mp4") || file.EndsWith(".mkv"))
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

        // ================= SEEK =================

        private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                var d = VideoPlayer.NaturalDuration.TimeSpan;
                DurationText.Text = d.ToString(@"mm\:ss");
                SeekBar.Maximum = d.TotalSeconds;
            }
        }

        private void UpdateSeekBar(object? sender, EventArgs e)
        {
            if (_isSeeking) return;

            if (VideoPlayer.Source != null && VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                SeekBar.Value = VideoPlayer.Position.TotalSeconds;
                CurrentTimeText.Text = VideoPlayer.Position.ToString(@"mm\:ss");
            }
        }

        private void SeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isSeeking)
                VideoPlayer.Position = TimeSpan.FromSeconds(SeekBar.Value);
        }

        private void SeekBar_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isSeeking = true;
        }

        private void SeekBar_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isSeeking = false;
            VideoPlayer.Position = TimeSpan.FromSeconds(SeekBar.Value);
        }

        // ================= TIMELINE =================

        private void RedrawTimeline()
        {
            TimelineCanvas.Children.Clear();

            double x = 10;

            foreach (var file in _files)
            {
                var img = new Image
                {
                    Width = 100,
                    Height = 60,
                    Stretch = Stretch.Fill,
                    Source = GetThumbnail(file)
                };

                Canvas.SetLeft(img, x);
                Canvas.SetTop(img, 50);

                TimelineCanvas.Children.Add(img);

                x += 110;
            }
        }

        private BitmapImage GetThumbnail(string file)
        {
            if (_thumbCache.ContainsKey(file))
                return _thumbCache[file];

            string temp = IOPath.Combine(IOPath.GetTempPath(), IOPath.GetFileName(file) + ".jpg");

            if (!File.Exists(temp))
            {
                if (file.EndsWith(".mp4") || file.EndsWith(".mkv"))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = @"C:\_Portable_\ffmpeg\bin\ffmpeg.exe",
                        Arguments = $"-y -i \"{file}\" -ss 00:00:01 -vframes 1 \"{temp}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    })?.WaitForExit();
                }
                else
                {
                    File.Copy(file, temp, true);
                }
            }

            var bmp = new BitmapImage(new Uri(temp));
            _thumbCache[file] = bmp;
            return bmp;
        }

        // ================= 🎬 EDIT FUNCTIONS =================

        private void Trim_Click(object sender, RoutedEventArgs e)
        {
            var f = GetCurrentFile();
            if (f == null) return;

            var r = _edit.Trim(f, GetOut("trim.mp4"), "00:00:02", "00:00:05");
            LogBox.Text = r.Item2;
        }

        private void Split_Click(object sender, RoutedEventArgs e)
        {
            var f = GetCurrentFile();
            if (f == null) return;

            var r = _edit.Split(f, GetOut("part_%03d.mp4"), 5);
            LogBox.Text = r.Item2;
        }

        private void Crop_Click(object sender, RoutedEventArgs e)
        {
            var f = GetCurrentFile();
            if (f == null) return;

            var r = _edit.Crop(f, GetOut("crop.mp4"), 300, 300, 0, 0);
            LogBox.Text = r.Item2;
        }

        private void Probe_Click(object sender, RoutedEventArgs e)
        {
            var f = GetCurrentFile();
            if (f == null) return;

            var r = _ffmpeg.Probe(f);
            LogBox.Text = r.Item2;
        }

        // ================= ENCODE / DECODE =================

        private void Encode_Click(object sender, RoutedEventArgs e)
        {
            var f = GetCurrentFile();
            if (f == null) return;

            string dir = EncodeOutputPath.Text;
            Directory.CreateDirectory(dir);

            string output = IOPath.Combine(dir, "frame_%05d.png");

            var r = _ffmpeg.Run($"-i \"{f}\" \"{output}\"");
            LogBox.Text = r.Item2;
        }

        private void Decode_Click(object sender, RoutedEventArgs e)
        {
            string dir = DecodeInputPath.Text;

            if (!Directory.Exists(dir))
            {
                LogBox.Text = "Folder not found";
                return;
            }

            string output = IOPath.Combine(dir, "output.mp4");

            var r = _ffmpeg.Run($"-framerate 30 -i \"{dir}\\frame_%05d.png\" -c:v libx264 \"{output}\"");
            LogBox.Text = r.Item2;
        }

        private void SelectEncodeFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                CheckFileExists = false,
                FileName = "Select folder"
            };

            if (dialog.ShowDialog() == true)
                EncodeOutputPath.Text = IOPath.GetDirectoryName(dialog.FileName);
        }

        private void SelectDecodeFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                CheckFileExists = false,
                FileName = "Select folder"
            };

            if (dialog.ShowDialog() == true)
                DecodeInputPath.Text = IOPath.GetDirectoryName(dialog.FileName);
        }

        private string GetOut(string name)
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