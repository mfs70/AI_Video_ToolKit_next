using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace AI_Video_ToolKit.UI.Services
{
    public class BufferedVideoPlayer
    {
        private const string FFmpegPath = @"C:\_Portable_\ffmpeg\bin\ffmpeg.exe";

        private Process? _process;
        private CancellationTokenSource? _cts;

        private readonly ConcurrentQueue<BitmapImage> _queue = new();

        private volatile bool _isRunning;
        private volatile bool _isPaused;

        private double _fps;
        private TimeSpan _currentTime;

        public event Action<BitmapImage>? OnFrame;
        public event Action<TimeSpan>? OnPositionChanged;
        public event Action? OnPlaybackEnded;

        // ================= START =================

        public void Start(string file, int width, int height, double fps, TimeSpan start)
        {
            Stop();

            _fps = fps <= 0 ? 25 : fps;
            _currentTime = start;

            _cts = new CancellationTokenSource();

            _isRunning = true;
            _isPaused = false;

            StartFFmpeg(file, width, height, start);

            Task.Run(() => DecodeLoop(_cts.Token));
            Task.Run(() => PlaybackLoop(_cts.Token));
        }

        // ================= FFMPEG =================

        private void StartFFmpeg(string file, int width, int height, TimeSpan start)
        {
            var psi = new ProcessStartInfo
            {
                FileName = FFmpegPath,
                Arguments =
                    $"-ss {start.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                    $"-i \"{file}\" " +
                    $"-vf scale={width}:{height}:force_original_aspect_ratio=decrease " +
                    "-f image2pipe -vcodec mjpeg -q:v 5 -",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _process = Process.Start(psi);
        }

        // ================= DECODE =================

        private async Task DecodeLoop(CancellationToken token)
        {
            if (_process == null) return;

            var stream = _process.StandardOutput.BaseStream;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var image = await ReadJpegFrame(stream, token);

                    if (image == null)
                    {
                        _isRunning = false;
                        OnPlaybackEnded?.Invoke();
                        return;
                    }

                    _queue.Enqueue(image);

                    // ограничение очереди
                    while (_queue.Count > 100)
                        _queue.TryDequeue(out _);
                }
            }
            catch { }
        }

        private async Task<BitmapImage?> ReadJpegFrame(Stream stream, CancellationToken token)
        {
            var ms = new MemoryStream();

            bool started = false;

            while (!token.IsCancellationRequested)
            {
                int b = stream.ReadByte();

                if (b == -1)
                    return null;

                // JPEG start
                if (!started && b == 0xFF)
                {
                    int next = stream.ReadByte();
                    if (next == 0xD8)
                    {
                        ms.WriteByte((byte)b);
                        ms.WriteByte((byte)next);
                        started = true;
                    }
                }
                else if (started)
                {
                    ms.WriteByte((byte)b);

                    // JPEG end
                    if (b == 0xD9)
                        break;
                }
            }

            ms.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();

            return bmp;
        }

        // ================= PLAYBACK =================

        private async Task PlaybackLoop(CancellationToken token)
        {
            double frameMs = 1000.0 / _fps;

            while (!token.IsCancellationRequested)
            {
                if (!_isRunning)
                {
                    await Task.Delay(5, token);
                    continue;
                }

                if (_isPaused)
                {
                    await Task.Delay(10, token);
                    continue;
                }

                var start = Stopwatch.GetTimestamp();

                if (_queue.TryDequeue(out var frame))
                {
                    OnFrame?.Invoke(frame);

                    _currentTime += TimeSpan.FromMilliseconds(frameMs);
                    OnPositionChanged?.Invoke(_currentTime);
                }
                else
                {
                    // НЕТ стопа — просто ждём
                    await Task.Delay(2, token);
                    continue;
                }

                var elapsedMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                var delay = frameMs - elapsedMs;

                if (delay > 1)
                    await Task.Delay((int)delay, token);
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
            _isRunning = false;

            _cts?.Cancel();
            _queue.Clear();

            try
            {
                if (_process != null && !_process.HasExited)
                    _process.Kill();
            }
            catch { }
        }
    }
}