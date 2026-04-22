using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
        // LOG
        // =====================================================
        private void Log(string message)
        {
            LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            LogBox.ScrollToEnd();
        }

        // =====================================================
        // INPUT FILE
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
        // OUTPUT FOLDER (ЧИСТЫЙ WPF через Windows API)
        // =====================================================
        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var path = FolderPicker();

            if (!string.IsNullOrEmpty(path))
            {
                OutputPathBox.Text = path;
                Log("Output selected");
            }
        }

        // =====================================================
        // FFPROBE
        // =====================================================
        private void Probe_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string ffprobe = @"C:\_Portable_\ffmpeg\bin\ffprobe.exe";

                var process = new Process();
                process.StartInfo.FileName = ffprobe;
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
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
            }
        }

        // =====================================================
        // PREVIEW
        // =====================================================
        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            string ffplay = @"C:\_Portable_\ffmpeg\bin\ffplay.exe";

            Process.Start(ffplay, $"\"{InputPathBox.Text}\"");

            Log("Preview started");
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

        // =====================================================
        // CLEAN WPF FOLDER PICKER (NO WINFORMS)
        // =====================================================
        private string FolderPicker()
        {
            var dialog = (IFileDialog)new FileOpenDialog();

            dialog.SetOptions(FOS.FOS_PICKFOLDERS | FOS.FOS_FORCEFILESYSTEM);

            if (dialog.Show(IntPtr.Zero) == 0)
            {
                dialog.GetResult(out IShellItem item);
                item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out IntPtr pathPtr);

                string path = Marshal.PtrToStringAuto(pathPtr);
                Marshal.FreeCoTaskMem(pathPtr);

                return path;
            }

            return null;
        }

        // =====================================================
        // WINDOWS API (COM)
        // =====================================================

        [ComImport]
        [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialog { }

        [ComImport]
        [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes();
            void SetFileTypeIndex();
            void GetFileTypeIndex();
            void Advise();
            void Unadvise();
            void SetOptions(FOS fos);
            void GetOptions();
            void SetDefaultFolder();
            void SetFolder();
            void GetFolder();
            void GetCurrentSelection();
            void SetFileName();
            void GetFileName();
            void SetTitle();
            void SetOkButtonLabel();
            void SetFileNameLabel();
            void GetResult(out IShellItem item);
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler();
            void GetParent();
            void GetDisplayName(SIGDN sigdnName, out IntPtr ppszName);
        }

        private enum SIGDN : uint
        {
            SIGDN_FILESYSPATH = 0x80058000
        }

        [Flags]
        private enum FOS : uint
        {
            FOS_PICKFOLDERS = 0x00000020,
            FOS_FORCEFILESYSTEM = 0x00000040
        }
    }
}