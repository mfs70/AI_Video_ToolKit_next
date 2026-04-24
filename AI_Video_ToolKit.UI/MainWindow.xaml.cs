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
        private List<string> _displayList = new List<string>();

        private Dictionary<string, string> _mediaInfo = new();

        private int _currentIndex = 0;

        public MainWindow()
        {
            InitializeComponent();
        }

        // ================= LOAD =================
        private void LoadFiles(IEnumerable<string> paths)
        {
            _files.Clear();
            _displayList.Clear();
            _mediaInfo.Clear();

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

            for (int i = 0; i < _files.Count; i++)
                FileSelector.Items.Add(_displayList[i]);

            if (_files.Count > 0)
                FileSelector.SelectedIndex = 0;
        }

        private void AddFile(string file)
        {
            if (!IsVideo(file) && !IsImage(file)) return;

            _files.Add(file);

            string info = "";

            if (IsVideo(file))
                info = AnalyzeVideo(file);
            else
                info = "Image";

            _mediaInfo[file] = info;

            _displayList.Add($"{Path.GetFileName(file)}  |  {info}");

            if (IsImage(file))
                AddImage(file);
        }

        // ================= PLAYER FIX =================
        private void PlayCurrent()
        {
            if (_currentIndex < 0 || _currentIndex >= _files.Count)
                return;

            string file = _files[_currentIndex];

            if (!IsVideo(file)) return;

            VideoPlayer.Stop();
            VideoPlayer.Source = null;
            VideoPlayer.Source = new Uri(file);
            VideoPlayer.Play();
        }

        private void FileSelector_Changed(object sender, SelectionChangedEventArgs e)
        {
            _currentIndex = FileSelector.SelectedIndex;
            PlayCurrent();
        }

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

        // ================= FFPROBE =================
        private string AnalyzeVideo(string file)
        {
            try
            {
                string ffprobe = @"C:\_Portable_\ffmpeg\bin\ffprobe.exe";

                var p = new Process();
                p.StartInfo.FileName = ffprobe;
                p.StartInfo.Arguments = $"-v quiet -print_format json -show_streams \"{file}\"";
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.UseShellExecute = false;

                p.Start();
                string json = p.StandardOutput.ReadToEnd();
                p.WaitForExit();

                var root = JsonNode.Parse(json);
                var streams = root?["streams"]?.AsArray();

                var v = streams?.FirstOrDefault(s => s?["codec_type"]?.ToString() == "video");

                if (v == null) return "No video";

                string w = v["width"]?.ToString() ?? "?";
                string h = v["height"]?.ToString() ?? "?";
                string fpsRaw = v["r_frame_rate"]?.ToString() ?? "0/1";

                double fps = ParseFps(fpsRaw);

                return $"{w}x{h} {fps:F1}fps";
            }
            catch
            {
                return "error";
            }
        }

        private double ParseFps(string val)
        {
            var p = val.Split('/');
            if (p.Length == 2 &&
                double.TryParse(p[0], out double a) &&
                double.TryParse(p[1], out double b) && b != 0)
                return a / b;

            return 0;
        }

        // ================= IMAGE =================
        private void AddImage(string path)
        {
            var img = new Image
            {
                Source = new BitmapImage(new Uri(path)),
                Width = 90
            };

            ImageTimeline.Children.Add(img);
        }

        // ================= TYPES =================
        private bool IsVideo(string f) => f.EndsWith(".mp4") || f.EndsWith(".mkv");
        private bool IsImage(string f) => f.EndsWith(".png") || f.EndsWith(".jpg");

        // ================= UI =================
        private void BrowseInput_Click(object sender, RoutedEventArgs e)
        {
            var d = new Microsoft.Win32.OpenFileDialog { Multiselect = true };
            if (d.ShowDialog() == true)
                LoadFiles(d.FileNames);
        }

        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var d = new Microsoft.Win32.SaveFileDialog();
            if (d.ShowDialog() == true)
                OutputPathBox.Text = Path.GetDirectoryName(d.FileName);
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            LoadFiles(files);
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Copy;
        }

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