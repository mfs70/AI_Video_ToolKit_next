using System;
using System.Buffers;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AI_Video_ToolKit.UI.Services
{
    public sealed class BufferedVideoPlayer
    {
        private const string FFmpegPath = @"C:\_Portable_\ffmpeg\bin\ffmpeg.exe";

        private Process? _process;
        private CancellationTokenSource? _cts;

        private int _width;
        private int _height;
        private int _stride;
        private int _frameSize;
        private double _fps;
        private double _speed = 1.0;

        private volatile bool _isPaused;

        private Channel<byte[]>? _frameChannel;
        private int _bufferedFrameCapacity = 20;

        private long _presentedFrames;
        private TimeSpan _startTime;
        private TimeSpan _lastPosition;

        public event Action<BitmapSource>? OnFrame;
        public event Action<TimeSpan>? OnPositionChanged;
        public event Action? OnPlaybackEnded;

        public void Start(string file, int width, int height, double fps, TimeSpan start, double speed = 1.0)
        {
            Stop();

            _width = Math.Max(16, width);
            _height = Math.Max(16, height);
            _stride = _width * 3;
            _frameSize = _stride * _height;

            _fps = fps > 0 ? fps : 25.0;
            _speed = speed > 0 ? speed : 1.0;

            _startTime = start;
            _lastPosition = start;
            _presentedFrames = 0;
            _isPaused = false;

            _bufferedFrameCapacity = CalculateBufferCapacity(_width, _height);
            _frameChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(_bufferedFrameCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true
            });

            _cts = new CancellationTokenSource();
            StartFFmpeg(file, start);

            _ = Task.Run(() => DecodeLoop(_cts.Token));
            _ = Task.Run(() => PlaybackLoop(_cts.Token));
        }

        private static int CalculateBufferCapacity(int width, int height)
        {
            // Keep approx 200-260 MB raw buffer budget.
            long bytesPerFrame = (long)width * height * 3;
            if (bytesPerFrame <= 0) return 12;

            long targetBudget = 230L * 1024 * 1024;
            int cap = (int)(targetBudget / bytesPerFrame);
            return Math.Clamp(cap, 6, 80);
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
            if (_process == null)
                throw new InvalidOperationException("Failed to start FFmpeg process.");
        }

        private async Task DecodeLoop(CancellationToken token)
        {
            if (_process == null || _frameChannel == null)
                return;

            var stream = _process.StandardOutput.BaseStream;
            var writer = _frameChannel.Writer;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(_frameSize);
                    int read = 0;

                    while (read < _frameSize)
                    {
                        int r = await stream.ReadAsync(buffer, read, _frameSize - read, token);
                        if (r == 0)
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                            writer.TryComplete();
                            return;
                        }
                        read += r;
                    }

                    await writer.WriteAsync(buffer, token);
                }
            }
            catch (OperationCanceledException)
            {
                writer.TryComplete();
            }
            catch (Exception ex)
            {
                writer.TryComplete(ex);
            }
        }

        private async Task PlaybackLoop(CancellationToken token)
        {
            if (_frameChannel == null)
                return;

            var reader = _frameChannel.Reader;
            var frameDelayMs = (int)Math.Max(1, 1000.0 / Math.Max(0.0001, _fps * _speed));

            try
            {
                while (await reader.WaitToReadAsync(token))
                {
                    while (reader.TryRead(out var rentedBuffer))
                    {
                        try
                        {
                            while (_isPaused && !token.IsCancellationRequested)
                                await Task.Delay(8, token);

                            if (token.IsCancellationRequested)
                                return;

                            var bmp = BitmapSource.Create(
                                _width,
                                _height,
                                96,
                                96,
                                PixelFormats.Bgr24,
                                null,
                                rentedBuffer,
                                _stride);

                            bmp.Freeze();
                            OnFrame?.Invoke(bmp);

                            _presentedFrames++;
                            _lastPosition = _startTime + TimeSpan.FromSeconds(_presentedFrames / _fps);
                            OnPositionChanged?.Invoke(_lastPosition);

                            await Task.Delay(frameDelayMs, token);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(rentedBuffer);
                        }
                    }
                }

                OnPlaybackEnded?.Invoke();
            }
            catch (OperationCanceledException)
            {
                // Normal on stop.
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

                _frameChannel = null;
                _isPaused = false;
            }
        }

        public TimeSpan GetCurrentPosition() => _lastPosition;
    }
}