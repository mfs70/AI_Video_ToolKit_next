using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AI_Video_ToolKit.UI.Services;

namespace AI_Video_ToolKit.UI
{
    public partial class MainWindow : Window
    {
        private FFmpegService _ffmpeg = new FFmpegService();
        private DispatcherTimer _timer = new DispatcherTimer();

        // ✅ ВАЖНО: длительность
        private TimeSpan _totalDuration = TimeSpan.Zero;

        public MainWindow()
        {
            InitializeComponent();

            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.Tick += UpdateTimeline;

            _ffmpeg.OnOutput += (text) =>
                Dispatcher.Invoke(() => Log(text));

            _ffmpeg.OnError += (text) =>
                Dispatcher.Invoke(() =>
                {
                    Log(text);
                    ParseFFmpegProgress(text);
                });
        }

        // ================= LOG =================
        private void Log(string msg)
        {
            LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            LogBox.ScrollToEnd();
        }

        // ================= INPUT =================

        private void BrowseInput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog();

            if (dialog.ShowDialog() == true)
            {
                InputPathBox.Text = dialog.FileName;
                Log("Input selected");
            }
        }

        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Select output folder",
                FileName = "Select Folder",
                Filter = "Folder|*.folder"
            };

            if (dialog.ShowDialog() == true)
            {
                string? path = Path.GetDirectoryName(dialog.FileName);

                if (!string.IsNullOrEmpty(path))
                {
                    OutputPathBox.Text = path;
                    Log("Output selected");
                }
            }
        }

        // ================= VIDEO =================

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            Play_Click(sender, e);
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(InputPathBox.Text))
            {
                Log("File not found");
                return;
            }

            VideoPlayer.Source = new Uri(InputPathBox.Text);
            VideoPlayer.Play();
            _timer.Start();
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            VideoPlayer.Pause();
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            VideoPlayer.Stop();
            _timer.Stop();
        }

        private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                TimelineSlider.Maximum =
                    VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            }
        }

        private void UpdateTimeline(object? sender, EventArgs e)
        {
            if (VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                TimelineSlider.Value = VideoPlayer.Position.TotalSeconds;

                TimeText.Text =
                    $"{VideoPlayer.Position:mm\\:ss} / {VideoPlayer.NaturalDuration.TimeSpan:mm\\:ss}";
            }
        }

        private void TimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Math.Abs(VideoPlayer.Position.TotalSeconds - e.NewValue) > 1)
            {
                VideoPlayer.Position = TimeSpan.FromSeconds(e.NewValue);
            }
        }

        // ================= PROBE =================

        private async void Probe_Click(object sender, RoutedEventArgs e)
        {
            string input = InputPathBox.Text;

            if (!File.Exists(input))
            {
                Log("File not found");
                return;
            }

            string ffprobe = @"C:\_Portable_\ffmpeg\bin\ffprobe.exe";

            string args =
                $"-v quiet -print_format json -show_format -show_streams \"{input}\"";

            await _ffmpeg.RunProcessAsync(ffprobe, args);
        }

        // ================= GET DURATION =================

        private async Task GetDurationAsync(string inputFile)
        {
            string ffprobe = @"C:\_Portable_\ffmpeg\bin\ffprobe.exe";

            var process = new Process();
            process.StartInfo.FileName = ffprobe;
            process.StartInfo.Arguments =
                $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{inputFile}\"";

            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;

            process.Start();

            string output = await process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();

            if (double.TryParse(output, System.Globalization.CultureInfo.InvariantCulture, out double seconds))
            {
                _totalDuration = TimeSpan.FromSeconds(seconds);
            }
        }

        // ================= ENCODE =================

        private async void Encode_Click(object sender, RoutedEventArgs e)
        {
            string input = InputPathBox.Text;

            if (!File.Exists(input))
            {
                Log("Input file not found");
                return;
            }

            string outputDir = OutputPathBox.Text;

            if (!Directory.Exists(outputDir))
            {
                Log("Output folder invalid");
                return;
            }

            string fileName = Path.GetFileNameWithoutExtension(input);

            string outputFile = Path.Combine(
                outputDir,
                $"{fileName}_encoded_{DateTime.Now:HHmmss}.mp4"
            );

            ProgressBar.Value = 0;

            Log("Getting duration...");
            await GetDurationAsync(input);

            Log($"Duration: {_totalDuration}");

            string ffmpeg = @"C:\_Portable_\ffmpeg\bin\ffmpeg.exe";

            string args =
                $"-y -i \"{input}\" -c:v libx264 \"{outputFile}\"";

            Log("Encoding started");

            await _ffmpeg.RunProcessAsync(ffmpeg, args);

            ProgressBar.Value = 100;
            StatusText.Text = "Done";
        }

        // ================= PROGRESS =================

        private void ParseFFmpegProgress(string line)
        {
            if (!line.Contains("time=")) return;

            try
            {
                var t = line.Split("time=")[1].Split(' ')[0];

                if (TimeSpan.TryParse(t, out var current) && _totalDuration.TotalSeconds > 0)
                {
                    double p = current.TotalSeconds / _totalDuration.TotalSeconds * 100;

                    ProgressBar.Value = p;
                    StatusText.Text = $"Encoding... {p:F1}%";
                }
            }
            catch { }
        }
    }
}