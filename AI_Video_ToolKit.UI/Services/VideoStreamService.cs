using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AI_Video_ToolKit.UI.Services
{
    public class VideoStreamService
    {
        private const string FFmpegPath = @"C:\_Portable_\ffmpeg\bin\ffmpeg.exe";

        private CancellationTokenSource? _cts;

        public event Action<BitmapSource>? OnFrame;

        private int _width;
        private int _height;

        public void Start(string file, int width, int height, TimeSpan startTime)
        {
            Stop();

            _width = width;
            _height = height;

            _cts = new CancellationTokenSource();

            Task.Run(() => RunStream(file, startTime, _cts.Token));
        }

        private async Task RunStream(string file, TimeSpan start, CancellationToken token)
        {
            var psi = new ProcessStartInfo
            {
                FileName = FFmpegPath,
                Arguments =
                    $"-ss {start} -i \"{file}\" " +
                    $"-vf scale={_width}:{_height} " +
                    "-f rawvideo -pix_fmt bgr24 -",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;

            int frameSize = _width * _height * 3;
            var stream = process.StandardOutput.BaseStream;
            byte[] buffer = new byte[frameSize];

            while (!token.IsCancellationRequested)
            {
                int read = 0;

                while (read < frameSize)
                {
                    int r = await stream.ReadAsync(buffer, read, frameSize - read, token);
                    if (r == 0) return;
                    read += r;
                }

                var bmp = BitmapSource.Create(
                    _width,
                    _height,
                    96,
                    96,
                    PixelFormats.Bgr24,
                    null,
                    buffer,
                    _width * 3);

                bmp.Freeze();

                OnFrame?.Invoke(bmp);
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
        }
    }
}