using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AI_Video_ToolKit.UI.Services
{
    public class BufferedVideoPlayer
    {
        private const string FFmpegPath = @"C:\_Portable_\ffmpeg\bin\ffmpeg.exe";

        private CancellationTokenSource? _cts;
        private Process? _process;

        private int _width;
        private int _height;
        private double _fps;

        private Stopwatch _clock = new();
        private bool _paused = false;

        public event Action<BitmapSource>? OnFrame;
        public event Action<TimeSpan>? OnPositionChanged;

        // 🔥 НОВОЕ
        public event Action? OnPlaybackEnded;

        public void Start(string file, int width, int height, double fps, TimeSpan start)
        {
            Stop();

            _width = width;
            _height = height;
            _fps = fps;

            _cts = new CancellationTokenSource();

            _clock.Restart();
            _paused = false;

            Task.Run(() => DecodeLoop(file, start, _cts.Token));
        }

        public void Pause()
        {
            _paused = true;
        }

        public void Resume()
        {
            _paused = false;
        }

        private async Task DecodeLoop(string file, TimeSpan start, CancellationToken token)
        {
            var psi = new ProcessStartInfo
            {
                FileName = FFmpegPath,
                Arguments =
                    $"-ss {start} -i \"{file}\" " +
                    $"-vf scale={_width}:{_height}:force_original_aspect_ratio=decrease," +
                    $"pad={_width}:{_height}:(ow-iw)/2:(oh-ih)/2,fps={_fps} " +
                    "-f rawvideo -pix_fmt bgr24 -",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _process = Process.Start(psi);

            var stream = _process!.StandardOutput.BaseStream;
            int frameSize = _width * _height * 3;

            byte[] buffer = new byte[frameSize];

            int delay = (int)(1000.0 / _fps);

            while (!token.IsCancellationRequested)
            {
                if (_paused)
                {
                    await Task.Delay(50);
                    continue;
                }

                int read = 0;

                while (read < frameSize)
                {
                    int r = await stream.ReadAsync(buffer, read, frameSize - read, token);

                    if (r == 0)
                    {
                        // 🔥 КОНЕЦ ВИДЕО
                        OnPlaybackEnded?.Invoke();
                        return;
                    }

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

                OnPositionChanged?.Invoke(_clock.Elapsed + start);

                await Task.Delay(delay, token);
            }
        }

        public void Stop()
        {
            _cts?.Cancel();

            if (_process != null && !_process.HasExited)
                _process.Kill();

            _clock.Reset();
            _paused = false;
        }
    }
}