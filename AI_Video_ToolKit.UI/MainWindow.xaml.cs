using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace AI_Video_ToolKit.UI
{
    public partial class MainWindow : Window
    {
        // ================= DATA =================
        // Список файлов (полные пути)
        private List<string> _files = new();

        // Список строк для отображения в UI
        private List<string> _display = new();

        // Индекс текущего файла
        private int _currentIndex = 0;

        // Пути к портативным инструментам
        private string FFMPEG = @"C:\_Portable_\ffmpeg\bin\ffmpeg.exe";
        private string FFPROBE = @"C:\_Portable_\ffmpeg\bin\ffprobe.exe";

        public MainWindow()
        {
            InitializeComponent();
        }

        // ================= LOAD FILES =================
        // Загружаем файлы/папки
        private void LoadFiles(IEnumerable<string> paths)
        {
            _files.Clear();
            _display.Clear();

            FileSelector.Items.Clear();
            ImageTimeline.Children.Clear();

            foreach (var p in paths)
            {
                if (Directory.Exists(p))
                {
                    foreach (var f in Directory.GetFiles(p))
                        AddFile(f);
                }
                else
                {
                    AddFile(p);
                }
            }

            // Обновляем UI список
            foreach (var item in _display)
                FileSelector.Items.Add(item);

            if (_files.Count > 0)
                FileSelector.SelectedIndex = 0;
        }

        // Добавление файла
        private void AddFile(string file)
        {
            if (!IsVideo(file) && !IsImage(file))
                return;

            _files.Add(file);

            if (IsVideo(file))
            {
                string info = GetVideoInfo(file);
                _display.Add($"{Path.GetFileName(file)} | {info}");
            }
            else
            {
                _display.Add($"{Path.GetFileName(file)} | image");
                AddImage(file);
            }
        }

        // ================= FFPROBE =================
        // Получение параметров видео
        private string GetVideoInfo(string file)
        {
            try
            {
                var p = new Process();

                p.StartInfo.FileName = FFPROBE;
                p.StartInfo.Arguments = $"-v quiet -print_format json -show_streams \"{file}\"";
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.UseShellExecute = false;

                p.Start();

                string json = p.StandardOutput.ReadToEnd();
                p.WaitForExit();

                var root = JsonNode.Parse(json);
                var streams = root?["streams"]?.AsArray();

                var v = streams?.FirstOrDefault(s => s?["codec_type"]?.ToString() == "video");

                if (v == null) return "no video";

                string w = v["width"]?.ToString() ?? "?";
                string h = v["height"]?.ToString() ?? "?";

                string fpsRaw = v["r_frame_rate"]?.ToString() ?? "0/1";
                double fps = ParseFps(fpsRaw);

                return $"{w}x{h} {fps:F2}fps";
            }
            catch
            {
                return "error";
            }
        }

        // Парсинг FPS вида "30000/1001"
        private double ParseFps(string val)
        {
            var parts = val.Split('/');

            if (parts.Length == 2 &&
                double.TryParse(parts[0], out double a) &&
                double.TryParse(parts[1], out double b) &&
                b != 0)
            {
                return a / b;
            }

            return 0;
        }

        // ================= PLAYER =================

        private void PlayCurrent()
        {
            if (_currentIndex < 0 || _currentIndex >= _files.Count)
                return;

            string file = _files[_currentIndex];

            if (!IsVideo(file)) return;

            try
            {
                VideoPlayer.Stop();
                VideoPlayer.Source = null;
                VideoPlayer.Source = new Uri(file);
                VideoPlayer.Play();
            }
            catch
            {
                StatusText.Text = "Player error";
            }
        }

        private void FileSelector_Changed(object sender, SelectionChangedEventArgs e)
        {
            _currentIndex = FileSelector.SelectedIndex;
            PlayCurrent();
        }

        // Кнопки управления плеером
        private void Play_Click(object sender, RoutedEventArgs e) => PlayCurrent();
        private void Pause_Click(object sender, RoutedEventArgs e) => VideoPlayer.Pause();
        private void Stop_Click(object sender, RoutedEventArgs e) => VideoPlayer.Stop();

        private void Prev_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex > 0)
                FileSelector.SelectedIndex = --_currentIndex;
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex < _files.Count - 1)
                FileSelector.SelectedIndex = ++_currentIndex;
        }

        // ================= EXTRACT =================

        private async void ExtractFrames_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex < 0) return;

            string input = _files[_currentIndex];

            if (!IsVideo(input))
            {
                StatusText.Text = "Select video";
                return;
            }

            string output = GetOutputFolder();
            string frames = Path.Combine(output, "frames");

            Directory.CreateDirectory(frames);

            string args = $"-i \"{input}\" \"{frames}\\frame_%04d.png\"";

            bool ok = await RunFFmpeg(args);

            StatusText.Text = ok ? "Extract DONE" : "Extract FAILED";
        }

        // ================= BUILD =================

        private async void BuildVideo_Click(object sender, RoutedEventArgs e)
        {
            string output = GetOutputFolder();

            string args =
                $"-framerate 30 -i \"{output}\\frames\\frame_%04d.png\" -c:v libx264 \"{output}\\result.mp4\"";

            bool ok = await RunFFmpeg(args);

            StatusText.Text = ok ? "Build DONE" : "Build FAILED";
        }

        // ================= RUN FFMPEG =================

        private Task<bool> RunFFmpeg(string args)
        {
            return Task.Run(() =>
            {
                try
                {
                    var p = new Process();

                    p.StartInfo.FileName = FFMPEG;
                    p.StartInfo.Arguments = args;
                    p.StartInfo.RedirectStandardError = true;
                    p.StartInfo.UseShellExecute = false;

                    p.Start();
                    p.WaitForExit();

                    return p.ExitCode == 0;
                }
                catch
                {
                    return false;
                }
            });
        }

        // ================= OUTPUT =================

        private string GetOutputFolder()
        {
            string path = OutputPathBox.Text;

            if (string.IsNullOrWhiteSpace(path))
            {
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
            }

            Directory.CreateDirectory(path);

            return path;
        }

        // ================= IMAGE =================

        private void AddImage(string path)
        {
            var img = new System.Windows.Controls.Image
            {
                Source = new BitmapImage(new Uri(path)),
                Width = 80
            };

            ImageTimeline.Children.Add(img);
        }

        // ================= TYPES =================

        private bool IsVideo(string f) =>
            f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase);

        private bool IsImage(string f) =>
            f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase);

        // ================= UI =================

        private void BrowseInput_Click(object sender, RoutedEventArgs e)
        {
            var d = new Microsoft.Win32.OpenFileDialog { Multiselect = true };

            if (d.ShowDialog() == true && d.FileNames.Length > 0)
            {
                string? folder = Path.GetDirectoryName(d.FileNames[0]);

                if (!string.IsNullOrEmpty(folder))
                    InputPathBox.Text = folder;

                LoadFiles(d.FileNames);
            }
        }

        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var d = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "Select Folder"
            };

            if (d.ShowDialog() == true)
            {
                string? folder = Path.GetDirectoryName(d.FileName);

                if (!string.IsNullOrEmpty(folder))
                    OutputPathBox.Text = folder;
            }
        }

        // ================= DRAG & DROP =================

        private void Window_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;

            var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            LoadFiles(files);
        }

        private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
        }
    }
}