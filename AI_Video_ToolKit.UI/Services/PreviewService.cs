using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace AI_Video_ToolKit.UI.Services
{
    public class PreviewService
    {
        private const string FFmpegPath = @"C:\_Portable_\ffmpeg\bin\ffmpeg.exe";

        public async Task<BitmapImage> GetFrame(string file, System.TimeSpan time)
        {
            var psi = new ProcessStartInfo
            {
                FileName = FFmpegPath,
                Arguments = $"-ss {time} -i \"{file}\" -frames:v 1 -f image2pipe -vcodec bmp -",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = Process.Start(psi)!;

            using var ms = new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(ms);

            process.WaitForExit();

            if (ms.Length == 0)
                return new BitmapImage(); // защита

            ms.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze(); // 🔥 важно для UI

            return bmp;
        }
    }
}