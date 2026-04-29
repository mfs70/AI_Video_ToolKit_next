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
                        "-f rawvideo -pix_fmt bgr24 -",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var process = Process.Start(psi);
                if (process == null)
                    return null;

                int frameSize = width * height * 3;
                byte[] buffer = new byte[frameSize];

                var stream = process.StandardOutput.BaseStream;

                int read = 0;
                while (read < frameSize)
                {
                    int r = await stream.ReadAsync(buffer, read, frameSize - read);
                    if (r == 0) break;
                    read += r;
                }

                if (read != frameSize)
                    return null;

                var bmp = BitmapSource.Create(
                    width,
                    height,
                    96,
                    96,
                    PixelFormats.Bgr24,
                    null,
                    buffer,
                    width * 3);

                bmp.Freeze();

                return bmp;
            }
            catch
            {
                return null;
            }
        }
    }
}