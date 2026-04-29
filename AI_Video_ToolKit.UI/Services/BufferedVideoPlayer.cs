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

        private CancellationTokenSource? _cts;
        private Process? _process;

        private readonly ConcurrentQueue<BitmapSource> _buffer = new();

        private int _width;
        private int _height;
        private double _fps;

        private bool _isPaused = false;

        private TimeSpan _position;

        public event Action<BitmapSource>? OnFrame;
        public event Action<TimeSpan>? OnPositionChanged;
        public event Action? OnPlaybackEnded;

        public void Start(string file, int width, int height, double fps, TimeSpan start)
        {
            Stop();

            _width = width;
            _height = height;
            _fps = fps;

            _position = start;
            _isPaused = false;

            _cts = new CancellationTokenSource();

            Task.Run(() => DecodeLoop(file, start, _cts.Token));
            Task.Run(() => PlaybackLoop(_cts.Token));
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
            if (_process == null) return;

            var stream = _process.StandardOutput.BaseStream;

            int frameSize = _width * _height * 3;
            byte[] buffer = new byte[frameSize];

            while (!token.IsCancellationRequested)
            {
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

                var bmp = BitmapSource.Create(
                    _width, _height,
                    96, 96,
                    PixelFormats.Bgr24,
                    null,
                    buffer,
                    _width * 3);

                bmp.Freeze();

                _buffer.Enqueue(bmp);
            }
        }

        private async Task PlaybackLoop(CancellationToken token)
        {
            int delay = (int)(1000.0 / _fps);

            while (!token.IsCancellationRequested)
            {
                if (_isPaused)
                {
                    await Task.Delay(10, token);
                    continue;
                }

                if (_buffer.TryDequeue(out var frame))
                {
                    OnFrame?.Invoke(frame);

                    _position += TimeSpan.FromSeconds(1.0 / _fps);
                    OnPositionChanged?.Invoke(_position);
                }

                await Task.Delay(delay, token);
            }
        }

        // ================= CONTROL =================

        public void Pause()
        {
            _isPaused = true;
        }

        public void Resume()
        {
            _isPaused = false;
        }

        public void Stop()
        {
            _cts?.Cancel();
            _buffer.Clear();

            if (_process != null && !_process.HasExited)
                _process.Kill();
        }
    }
}