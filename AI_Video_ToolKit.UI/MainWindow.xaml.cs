using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            Directory.CreateDirectory(_inputDir);
            Directory.CreateDirectory(_outputDir);

            InputPathBox.Text = _inputDir;
            OutputPathBox.Text = _outputDir;
        }

        // ================= INPUT =================
        private void BrowseInput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select files",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                _files.Clear();
                FileSelector.Items.Clear();
                ImageTimeline.Children.Clear();

                // 🔥 используем именно выбранные файлы
                foreach (var file in dialog.FileNames)
                {
                    if (!IsVideo(file) && !IsImage(file))
                        continue;

                    _files.Add(file);
                    FileSelector.Items.Add(Path.GetFileName(file));

                    if (IsImage(file))
                        AddImage(file);
                }

                // 👉 показываем путь выбранного файла (или первого)
                InputPathBox.Text = dialog.FileNames.First();

                if (_files.Count > 0)
                {
                    _currentIndex = 0;
                    FileSelector.SelectedIndex = 0;
                }

                StatusText.Text = $"Loaded {_files.Count} files";
            }
        }

        // ================= OUTPUT =================
        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Select output folder",
                FileName = "Select Folder"
            };

            if (dialog.ShowDialog() == true)
            {
                string path = Path.GetDirectoryName(dialog.FileName) ?? "";

                if (!string.IsNullOrEmpty(path))
                {
                    OutputPathBox.Text = path;
                    StatusText.Text = "Output selected";
                }
            }
        }

        // ================= PLAYER =================
        private void FileSelector_Changed(object sender, SelectionChangedEventArgs e)
        {
            _currentIndex = FileSelector.SelectedIndex;
            PlayCurrent();
        }

        private void PlayCurrent()
        {
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
            {
                _currentIndex--;
                FileSelector.SelectedIndex = _currentIndex;
            }
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex < _files.Count - 1)
            {
                _currentIndex++;
                FileSelector.SelectedIndex = _currentIndex;
            }
        }

        // ================= TIMELINE =================
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
            return ext == ".mp4" || ext == ".mkv" || ext == ".avi" || ext == ".mov";
        }

        private bool IsImage(string f)
        {
            string ext = Path.GetExtension(f).ToLower();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp";
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