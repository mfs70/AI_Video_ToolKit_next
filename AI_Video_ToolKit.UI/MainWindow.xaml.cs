using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace AI_Video_ToolKit.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        // =====================================================
        // LOG HELPER
        // =====================================================
        private void Log(string message)
        {
            LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            LogBox.ScrollToEnd();
        }

        // =====================================================
        // INPUT SELECT
        // =====================================================
        private void BrowseInput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog();
            if (dialog.ShowDialog() == true)
            {
                InputPathBox.Text = dialog.FileName;
                Log("Input selected");
            }
        }

        // =====================================================
        // OUTPUT SELECT
        // =====================================================
        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                OutputPathBox.Text = dialog.SelectedPath;
                Log("Output selected");
            }
        }

        // =====================================================
        // PROBE (FFprobe)
        // =====================================================
        private void Probe_Click(object sender, RoutedEventArgs e)
        {
            Log("Running FFprobe...");

            string ffprobePath = @"C:\_Portable_\ffmpeg\bin\ffprobe.exe";

            var process = new Process();
            process.StartInfo.FileName = ffprobePath;
            process.StartInfo.Arguments =
                $"-v quiet -print_format json -show_format -show_streams \"{InputPathBox.Text}\"";

            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;

            process.Start();

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            MediaInfoBox.Text = output;

            Log("FFprobe done");
        }

        // =====================================================
        // PREVIEW
        // =====================================================
        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            string ffplay = @"C:\_Portable_\ffmpeg\bin\ffplay.exe";

            Process.Start(ffplay, $"\"{InputPathBox.Text}\"");

            Log("Preview launched");
        }

        // =====================================================
        // ENCODE
        // =====================================================
        private void Encode_Click(object sender, RoutedEventArgs e)
        {
            string ffmpeg = @"C:\_Portable_\ffmpeg\bin\ffmpeg.exe";

            string output = Path.Combine(OutputPathBox.Text, "output.mp4");

            Process.Start(ffmpeg,
                $"-y -i \"{InputPathBox.Text}\" -c:v libx264 \"{output}\"");

            Log("Encoding started");
        }
    }
}