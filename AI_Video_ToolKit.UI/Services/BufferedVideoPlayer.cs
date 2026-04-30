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

        private Process? _process;
        private CancellationTokenSource? _cts;

        private int _width;
        private int _height;
        private double _fps;

        private volatile bool _isPaused;

        private Stopwatch _clock = new();
        private TimeSpan _startTime;
        private TimeSpan _pauseOffset;

        public event Action<BitmapSource>? OnFrame;
        public event Action<TimeSpan>? OnPositionChanged;
        public event Action? OnPlaybackEnded;

        public void Start(string file, int width, int height, double fps, TimeSpan start)
        {
            Stop();

            _width = width;
            _height = height;
            _fps = fps <= 0 ? 25 : fps;

            _startTime = start;
            _pauseOffset = TimeSpan.Zero;

            _cts = new CancellationTokenSource();
            _isPaused = false;

            StartFFmpeg(file, start);

            _clock.Restart();

            Task.Run(() => ReadLoop(_cts.Token));
        }

        private void StartFFmpeg(string file, TimeSpan start)
        {
            var psi = new ProcessStartInfo
            {
                FileName = FFmpegPath,
                Arguments =
                    $"-ss {start.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                    $"-i \"{file}\" " +
                    $"-vf scale={_width}:{_height}:force_original_aspect_ratio=decrease," +
                    $"pad={_width}:{_height}:(ow-iw)/2:(oh-ih)/2 " +
                    "-f rawvideo -pix_fmt bgr24 -",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _process = Process.Start(psi);
        }

        private async Task ReadLoop(CancellationToken token)
        {
            if (_process == null) return;

            var stream = _process.StandardOutput.BaseStream;

            int frameSize = _width * _height * 3;
            byte[] buffer = new byte[frameSize];

            double frameTimeMs = 1000.0 / _fps;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (_isPaused)
                    {
                        await Task.Delay(10, token);
                        continue;
                    }

                    int read = 0;
                    while (read < frameSize)
                    {
                        int r = await stream.ReadAsync(buffer, read, frameSize - read, token);
                        if (r == 0)
                        {
                            OnPlaybackEnded?.Invoke();
                            return;
                        }
                        read += r;
                    }

                    var frameTime = _startTime + _pauseOffset + _clock.Elapsed;

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
                    OnPositionChanged?.Invoke(frameTime);

                    await Task.Delay((int)frameTimeMs, token);
                }
            }
            catch { }
        }

        public void Pause()
        {
            if (_isPaused) return;

            _pauseOffset += _clock.Elapsed;
            _clock.Reset();

            _isPaused = true;
        }

        public void Resume()
        {
            if (!_isPaused) return;

            _clock.Restart();
            _isPaused = false;
        }

        public void Stop()
        {
            _cts?.Cancel();

            try
            {
                if (_process != null && !_process.HasExited)
                    _process.Kill();
            }
            catch { }
        }
    }
}