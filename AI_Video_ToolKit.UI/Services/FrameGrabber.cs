using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AI_Video_ToolKit.UI.Services
{
    public class FrameGrabber
    {
        private const string FFmpegPath = @"C:\_Portable_\ffmpeg\bin\ffmpeg.exe";

        public async Task<BitmapSource?> GetFrame(string file, TimeSpan time, int width, int height)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bmp");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = FFmpegPath,
                    Arguments =
                        $"-ss {time} -i \"{file}\" " +
                        $"-frames:v 1 " +
                        $"-vf scale={width}:{height}:force_original_aspect_ratio=decrease," +
                        $"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2 " +
                        $"\"{tempFile}\" -y",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var process = Process.Start(psi);

                if (process == null)
                    return null;

                await process.WaitForExitAsync();

                if (!File.Exists(tempFile))
                    return null;

                var bmp = new BitmapImage();

                using (var stream = File.OpenRead(tempFile))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = stream;
                    bmp.EndInit();
                    bmp.Freeze();
                }

                return bmp;
            }
            catch
            {
                return null;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempFile))
                        File.Delete(tempFile);
                }
                catch { }
            }
        }
    }
}