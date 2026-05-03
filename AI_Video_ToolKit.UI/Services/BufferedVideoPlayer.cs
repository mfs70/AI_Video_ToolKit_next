using System;
using System.Collections.Generic;
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
        private const int MaxBufferedFrames = 30;

        private Process? _process;
        private CancellationTokenSource? _cts;

        private int _width;
        private int _height;
        private double _fps;
        private double _speed = 1.0;

        private volatile bool _isPaused;
        private volatile bool _decodeFinished;

        private readonly object _bufferLock = new();
        private readonly Queue<byte[]> _frameQueue = new();

        private long _decodedFrames;
        private long _presentedFrames;

        private TimeSpan _startTime;
        private TimeSpan _lastPosition;

        public event Action<BitmapSource>? OnFrame;
        public event Action<TimeSpan>? OnPositionChanged;
        public event Action? OnPlaybackEnded;

        public void Start(string file, int width, int height, double fps, TimeSpan start, double speed = 1.0)
        {
            Stop();

            _width = width;
            _height = height;
            _fps = fps > 0 ? fps : 25;
            _speed = speed > 0 ? speed : 1.0;

            _startTime = start;
            _lastPosition = start;
            _decodedFrames = 0;
            _presentedFrames = 0;
            _decodeFinished = false;
            _isPaused = false;

            _cts = new CancellationTokenSource();
            StartFFmpeg(file, start);

            Task.Run(() => DecodeLoop(_cts.Token));
            Task.Run(() => PlaybackLoop(_cts.Token));
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

        private async Task DecodeLoop(CancellationToken token)
        {
            if (_process == null) return;

            var stream = _process.StandardOutput.BaseStream;
            int frameSize = _width * _height * 3;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    lock (_bufferLock)
                    {
                        if (_frameQueue.Count >= MaxBufferedFrames)
                        {
                            Monitor.Wait(_bufferLock, 10);
                            continue;
                        }
                    }

                    var buffer = new byte[frameSize];
                    int read = 0;
                    while (read < frameSize)
                    {
                        int r = await stream.ReadAsync(buffer, read, frameSize - read, token);
                        if (r == 0)
                        {
                            _decodeFinished = true;
                            return;
                        }
                        read += r;
                    }

                    lock (_bufferLock)
                    {
                        _frameQueue.Enqueue(buffer);
                        _decodedFrames++;
                        Monitor.PulseAll(_bufferLock);
                    }
                }
            }
            catch
            {
                _decodeFinished = true;
            }
        }

        private async Task PlaybackLoop(CancellationToken token)
        {
            var frameDelayMs = (int)Math.Max(1, 1000.0 / Math.Max(0.0001, _fps * _speed));

            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (_isPaused)
                    {
                        await Task.Delay(10, token);
                        continue;
                    }

                    byte[]? frame = null;
                    lock (_bufferLock)
                    {
                        if (_frameQueue.Count > 0)
                        {
                            frame = _frameQueue.Dequeue();
                            Monitor.PulseAll(_bufferLock);
                        }
                    }

                    if (frame == null)
                    {
                        if (_decodeFinished)
                        {
                            OnPlaybackEnded?.Invoke();
                            return;
                        }

                        await Task.Delay(2, token);
                        continue;
                    }

                    var bmp = BitmapSource.Create(
                        _width,
                        _height,
                        96,
                        96,
                        PixelFormats.Bgr24,
                        null,
                        frame,
                        _width * 3);

                    bmp.Freeze();
                    OnFrame?.Invoke(bmp);

                    _presentedFrames++;
                    _lastPosition = _startTime + TimeSpan.FromSeconds(_presentedFrames / _fps);
                    OnPositionChanged?.Invoke(_lastPosition);

                    await Task.Delay(frameDelayMs, token);
                }
            }
            catch
            {
                // swallow on stop
            }
        }

        public void Pause()
        {
            if (_isPaused) return;
            _isPaused = true;
            OnPositionChanged?.Invoke(_lastPosition);
        }

        public void Resume()
        {
            if (!_isPaused) return;
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
            finally
            {
                _process?.Dispose();
                _process = null;

                _cts?.Dispose();
                _cts = null;

                lock (_bufferLock)
                {
                    _frameQueue.Clear();
                    Monitor.PulseAll(_bufferLock);
                }

                _decodeFinished = false;
                _isPaused = false;
            }
        }

        public TimeSpan GetCurrentPosition() => _lastPosition;
    }
}