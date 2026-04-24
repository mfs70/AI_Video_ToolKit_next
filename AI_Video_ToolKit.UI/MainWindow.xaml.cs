using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace AI_Video_ToolKit.UI
{
    public partial class MainWindow : Window
    {
        private List<string> _files = new List<string>();

        private string _inputDir = "";
        private string _outputDir = "";
        private string _confDir = "";

        private int _currentIndex = 0;

        public MainWindow()
        {
            InitializeComponent();
            InitEnvironment();
        }

        // ================= INIT =================
        private void InitEnvironment()
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;

            _inputDir = Path.Combine(root, "input");
            _outputDir = Path.Combine(root, "output");
            _confDir = Path.Combine(root, "conf");

            Directory.CreateDirectory(_inputDir);
            Directory.CreateDirectory(_outputDir);
            Directory.CreateDirectory(_confDir);

            InputPathBox.Text = _inputDir;
            OutputPathBox.Text = _outputDir;
        }

        // ================= DRAG & DROP =================
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;

            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            var dropped = (string[])e.Data.GetData(DataFormats.FileDrop);
            LoadFiles(dropped);
        }

        // ================= INPUT =================
        private void BrowseInput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
                LoadFiles(dialog.FileNames);
        }

        // ================= OUTPUT =================
        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "Select Folder"
            };

            if (dialog.ShowDialog() == true)
            {
                string path = Path.GetDirectoryName(dialog.FileName) ?? "";
                if (!string.IsNullOrEmpty(path))
                    OutputPathBox.Text = path;
            }
        }

        // ================= LOAD =================
        private void LoadFiles(IEnumerable<string> paths)
        {
            _files.Clear();
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

            if (_files.Count > 0)
            {
                InputPathBox.Text = _files[0];
                _currentIndex = 0;
                FileSelector.SelectedIndex = 0;
            }
        }

        private void AddFile(string file)
        {
            if (!IsVideo(file) && !IsImage(file))
                return;

            _files.Add(file);
            FileSelector.Items.Add(Path.GetFileName(file));

            if (IsImage(file))
                AddImage(file);

            if (IsVideo(file))
                AnalyzeVideo(file);
        }

        // ================= FFPROBE =================
        private void AnalyzeVideo(string file)
        {
            try
            {
                string ffprobe = @"C:\_Portable_\ffmpeg\bin\ffprobe.exe";

                var process = new Process();
                process.StartInfo.FileName = ffprobe;
                process.StartInfo.Arguments =
                    $"-v quiet -print_format json -show_streams \"{file}\"";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.UseShellExecute = false;

                process.Start();

                string json = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                SaveJson(file, json);
                ParseAndDisplay(json);
            }
            catch (Exception ex)
            {
                MediaInfoText.Text = "FFprobe error: " + ex.Message;
            }
        }

        // ================= SAVE JSON =================
        private void SaveJson(string file, string json)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            string path = Path.Combine(_confDir, name + ".json");

            File.WriteAllText(path, json);
        }

        // ================= SAFE PARSE =================
        private void ParseAndDisplay(string json)
        {
            try
            {
                var root = JsonNode.Parse(json);
                if (root == null) return;

                var streams = root["streams"]?.AsArray();
                if (streams == null) return;

                var video = streams.FirstOrDefault(s => s?["codec_type"]?.ToString() == "video");
                var audio = streams.FirstOrDefault(s => s?["codec_type"]?.ToString() == "audio");

                if (video == null)
                {
                    MediaInfoText.Text = "No video stream";
                    return;
                }

                string width = video["width"]?.ToString() ?? "?";
                string height = video["height"]?.ToString() ?? "?";

                string fpsRaw = video["r_frame_rate"]?.ToString() ?? "0/1";
                double fps = ParseFps(fpsRaw);

                string vcodec = video["codec_name"]?.ToString() ?? "?";
                string acodec = audio?["codec_name"]?.ToString() ?? "none";

                MediaInfoText.Text =
                    $"{width}x{height}\n{fps:F2} fps\nVideo: {vcodec}\nAudio: {acodec}";
            }
            catch
            {
                MediaInfoText.Text = "Parse error";
            }
        }

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
        private void FileSelector_Changed(object sender, SelectionChangedEventArgs e)
        {
            _currentIndex = FileSelector.SelectedIndex;

            if (_currentIndex < 0 || _currentIndex >= _files.Count)
                return;

            string file = _files[_currentIndex];

            if (IsVideo(file))
            {
                VideoPlayer.Source = new Uri(file);
                VideoPlayer.Play();
            }
        }

        private void Play_Click(object sender, RoutedEventArgs e) => VideoPlayer.Play();
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

        // ================= IMAGE =================
        private void AddImage(string path)
        {
            var img = new Image
            {
                Source = new BitmapImage(new Uri(path)),
                Width = 90,
                Margin = new Thickness(3)
            };

            var btn = new Button { Content = img };

            btn.Click += (s, e) =>
            {
                ImageTimeline.Children.Remove(btn);
            };

            ImageTimeline.Children.Add(btn);
        }

        // ================= TYPES =================
        private bool IsVideo(string f)
        {
            string ext = Path.GetExtension(f).ToLower();
            return ext == ".mp4" || ext == ".mkv" || ext == ".avi";
        }

        private bool IsImage(string f)
        {
            string ext = Path.GetExtension(f).ToLower();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg";
        }

        // ================= ACTIONS =================
        private void ExtractFrames_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Extract...";
        }

        private void BuildVideo_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Build...";
        }
    }
}