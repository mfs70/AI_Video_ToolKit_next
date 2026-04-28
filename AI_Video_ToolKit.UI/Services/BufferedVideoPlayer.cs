using System;
using System.Collections.Concurrent;
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

        private readonly ConcurrentQueue<(BitmapSource frame, double time)> _buffer = new();

        private CancellationTokenSource? _cts;
        private Process? _process;

        private int _width;
        private int _height;
        private double _fps;

        private string? _file;

        private Func<double>? _audioTimeProvider;
        private Action? _pauseAudio;
        private Action? _resumeAudio;

        public event Action<BitmapSource>? OnFrame;

        private const int MAX_BUFFER_FRAMES = 120;
        private const double SYNC_TOLERANCE = 0.04; // 40 ms

        public void Start(
            string file,
            int width,
            int height,
            double fps,
            TimeSpan start,
            Func<double> audioTime,
            Action pauseAudio,
            Action resumeAudio)
        {
            Stop();

            _file = file;
            _width = width;
            _height = height;
            _fps = fps;
            _audioTimeProvider = audioTime;
            _pauseAudio = pauseAudio;
            _resumeAudio = resumeAudio;

            _cts = new CancellationTokenSource();

            StartFFmpeg(start);

            Task.Run(() => ReadFrames(start.TotalSeconds, _cts.Token));
            Task.Run(() => PlaybackLoop(_cts.Token));
        }

        private void StartFFmpeg(TimeSpan start)
        {
            var psi = new ProcessStartInfo
            {
                FileName = FFmpegPath,
                Arguments =
                    $"-ss {start} -i \"{_file}\" " +
                    $"-vf scale={_width}:{_height},fps={_fps} " +
                    "-f rawvideo -pix_fmt bgr24 -",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _process = Process.Start(psi);
        }

        private async Task ReadFrames(double startTime, CancellationToken token)
        {
            if (_process == null) return;

            var stream = _process.StandardOutput.BaseStream;
            int frameSize = _width * _height * 3;

            byte[] buffer = new byte[frameSize];

            double time = startTime;
            double step = 1.0 / _fps;

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

                if (_buffer.Count < MAX_BUFFER_FRAMES)
                    _buffer.Enqueue((bmp, time));
                else
                {
                    _buffer.TryDequeue(out _);
                    _buffer.Enqueue((bmp, time));
                }

                time += step;
            }
        }

        private async Task PlaybackLoop(CancellationToken token)
        {
            bool paused = false;

            while (!token.IsCancellationRequested)
            {
                if (_buffer.IsEmpty)
                {
                    if (!paused)
                    {
                        _pauseAudio?.Invoke();
                        paused = true;
                    }

                    await Task.Delay(5);
                    continue;
                }

                if (paused)
                {
                    _resumeAudio?.Invoke();
                    paused = false;
                }

                double audioTime = _audioTimeProvider?.Invoke() ?? 0;

                // 🔥 DROP старые кадры
                while (_buffer.TryPeek(out var old) && old.time < audioTime - SYNC_TOLERANCE)
                {
                    _buffer.TryDequeue(out _);
                }

                if (_buffer.TryPeek(out var current))
                {
                    double delta = current.time - audioTime;

                    // 🔥 если кадр близок или чуть впереди → показываем
                    if (delta <= SYNC_TOLERANCE)
                    {
                        _buffer.TryDequeue(out var frame);
                        OnFrame?.Invoke(frame.frame);
                    }
                    // 🔥 если слишком впереди → ждём
                }

                await Task.Delay(5);
            }
        }

        public void Stop()
        {
            _cts?.Cancel();

            if (_process != null && !_process.HasExited)
                _process.Kill();

            _buffer.Clear();
        }
    }
}