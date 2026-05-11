using System;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AI_Video_ToolKit.Infrastructure.Services;

namespace AI_Video_ToolKit.UI.Services
{
    /// <summary>
    /// Сервис для захвата отдельного кадра из видео с помощью FFmpeg
    /// </summary>
    public class FrameGrabber
    {
        private readonly FFmpegProcessService _processService;

        public FrameGrabber(FFmpegProcessService processService)
        {
            _processService = processService;
        }

        public async Task<BitmapSource?> GetFrame(string filePath, TimeSpan position, int width, int height)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}.bmp");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _processService.FfmpegPath,
                    Arguments = $"-ss {position.TotalSeconds.ToString(CultureInfo.InvariantCulture)} -i \"{filePath}\" -vframes 1 -vf scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2 -f image2 \"{tempFile}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var process = Process.Start(psi);
                if (process != null)
                    await process.WaitForExitAsync();

                if (File.Exists(tempFile))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(tempFile);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FrameGrabber error: {ex.Message}");
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
            return null;
        }
    }
}
