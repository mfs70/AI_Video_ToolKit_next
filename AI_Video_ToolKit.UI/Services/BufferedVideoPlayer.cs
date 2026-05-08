// Файл: D:\AI_Video_ToolKit_next\AI_Video_ToolKit.UI\Services\BufferedVideoPlayer.cs
using System;
using System.Buffers;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.Wave;

namespace AI_Video_ToolKit.UI.Services
{
    /// <summary>
    /// Буферизированный видеоплеер с поддержкой аудио через FFmpeg.
    /// Преобразует любой аудиоформат (включая 6-канальный AC3) в стерео PCM 44.1 кГц.
    /// </summary>
    public sealed class BufferedVideoPlayer : IDisposable
    {
        // Путь к ffmpeg.exe – измените при необходимости
        private const string FFmpegPath = @"C:\_Portable_\ffmpeg\bin\ffmpeg.exe";

        private Process? _videoProcess;
        private Process? _audioProcess;
        private CancellationTokenSource? _cts;

        private int _width, _height, _stride, _frameSize;
        private double _fps, _speed = 1.0;
        private bool _isPaused;
        private Channel<byte[]>? _frameChannel;
        private int _bufferedFrameCapacity;

        private long _presentedFrames;
        private TimeSpan _startTime;
        private TimeSpan _lastPosition;

        private WaveOutEvent? _waveOut;
        private BufferedWaveProvider? _waveProvider;
        private bool _audioEnabled;
        private bool _videoEnded;
        private bool _audioEnded;
        private bool _stopping;

        public event Action<BitmapSource>? OnFrame;
        public event Action<TimeSpan>? OnPositionChanged;
        public event Action? OnPlaybackEnded;
        public Action<string>? LogCallback;

        public void Start(string file, int width, int height, double fps, TimeSpan start, double speed = 1.0, bool enableAudio = true)
        {
            try
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
                _videoEnded = false;
                _audioEnded = false;
                _audioEnabled = false;
                _stopping = false;

                _bufferedFrameCapacity = CalculateBufferCapacity(_width, _height);
                _frameChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(_bufferedFrameCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true
                });

                _cts = new CancellationTokenSource();
                StartVideoFFmpeg(file, start);
                if (enableAudio) StartAudioFFmpeg(file, start);

                _ = Task.Run(() => DecodeLoop(_cts.Token));
                _ = Task.Run(() => PlaybackLoop(_cts.Token));
            }
            catch (Exception ex)
            {
                LogCallback?.Invoke($"Start error: {ex.Message}");
            }
        }

        private static int CalculateBufferCapacity(int width, int height)
        {
            long bytesPerFrame = (long)width * height * 3;
            if (bytesPerFrame <= 0) return 12;
            long targetBudget = 230L * 1024 * 1024;
            int cap = (int)(targetBudget / bytesPerFrame);
            return Math.Clamp(cap, 6, 80);
        }

        private void StartVideoFFmpeg(string file, TimeSpan start)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = FFmpegPath,
                    Arguments = $"-ss {start.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                                $"-i \"{file}\" " +
                                $"-vf scale={_width}:{_height}:force_original_aspect_ratio=decrease," +
                                $"pad={_width}:{_height}:(ow-iw)/2:(oh-ih)/2 " +
                                "-f rawvideo -pix_fmt bgr24 -",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                _videoProcess = Process.Start(psi);
                if (_videoProcess != null)
                {
                    _videoProcess.EnableRaisingEvents = true;
                    _videoProcess.Exited += (_, _) =>
                    {
                        if (!_stopping) _videoEnded = true;
                        CheckPlaybackEnd();
                    };
                }
                else
                {
                    LogCallback?.Invoke("Failed to start FFmpeg video.");
                    _videoEnded = true;
                }
            }
            catch (Exception ex)
            {
                LogCallback?.Invoke($"Video FFmpeg error: {ex.Message}");
                _videoEnded = true;
            }
        }

        private void StartAudioFFmpeg(string file, TimeSpan start)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = FFmpegPath,
                    Arguments = $"-ss {start.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                                $"-i \"{file}\" " +
                                "-f s16le -acodec pcm_s16le -ar 44100 -ac 2 -",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                _audioProcess = Process.Start(psi);
                if (_audioProcess == null)
                {
                    LogCallback?.Invoke("Failed to start FFmpeg audio.");
                    _audioEnabled = false;
                    return;
                }

                _audioProcess.EnableRaisingEvents = true;
                _audioProcess.Exited += (_, _) =>
                {
                    if (!_stopping) _audioEnded = true;
                    CheckPlaybackEnd();
                };

                var waveFormat = new WaveFormat(44100, 16, 2);
                _waveProvider = new BufferedWaveProvider(waveFormat)
                {
                    BufferDuration = TimeSpan.FromMilliseconds(500),
                    DiscardOnBufferOverflow = false
                };
                _waveOut = new WaveOutEvent();
                _waveOut.Init(_waveProvider);
                _waveOut.Play();
                _audioEnabled = true;

                _ = Task.Run(() => AudioReadLoop(_cts!.Token));
                LogCallback?.Invoke("Audio initialized (FFmpeg -> stereo PCM).");
            }
            catch (Exception ex)
            {
                LogCallback?.Invoke($"Audio init error: {ex.Message}");
                _audioEnabled = false;
                CleanupAudio();
            }
        }

        private async Task AudioReadLoop(CancellationToken token)
        {
            if (_audioProcess == null || _waveProvider == null) return;
            var stream = _audioProcess.StandardOutput.BaseStream;
            byte[] buffer = new byte[16384];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    // Контроль заполнения буфера (не более 80%)
                    if ((double)_waveProvider.BufferedBytes / _waveProvider.BufferLength > 0.8)
                    {
                        await Task.Delay(20, token);
                        continue;
                    }

                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                    if (read == 0) break;

                    _waveProvider.AddSamples(buffer, 0, read);

                    // Динамическая задержка для синхронизации с реальным временем
                    double seconds = read / (44100.0 * 4);
                    int delay = (int)(seconds * 900);
                    if (delay > 0) await Task.Delay(delay, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogCallback?.Invoke($"Audio read error: {ex.Message}");
            }
            finally
            {
                if (!_stopping) _audioEnded = true;
                CheckPlaybackEnd();
            }
        }

        private void CheckPlaybackEnd()
        {
            if (_videoEnded && (!_audioEnabled || _audioEnded))
            {
                Stop();
                OnPlaybackEnded?.Invoke();
            }
        }

        private async Task DecodeLoop(CancellationToken token)
        {
            if (_videoProcess == null || _frameChannel == null) return;
            var stream = _videoProcess.StandardOutput.BaseStream;
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
                            if (!_stopping) _videoEnded = true;
                            CheckPlaybackEnd();
                            ArrayPool<byte>.Shared.Return(buffer);
                            writer.TryComplete();
                            return;
                        }
                        read += r;
                    }
                    await writer.WriteAsync(buffer, token);
                }
            }
            catch (OperationCanceledException) { writer.TryComplete(); }
            catch (Exception ex)
            {
                LogCallback?.Invoke($"Decode error: {ex.Message}");
                writer.TryComplete();
            }
        }

        private async Task PlaybackLoop(CancellationToken token)
        {
            if (_frameChannel == null) return;
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
                            if (token.IsCancellationRequested) return;

                            var bmp = BitmapSource.Create(_width, _height, 96, 96, PixelFormats.Bgr24, null, rentedBuffer, _stride);
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
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogCallback?.Invoke($"Playback error: {ex.Message}");
            }
        }

        public void Pause()
        {
            if (_isPaused) return;
            _isPaused = true;
            _waveOut?.Pause();
        }

        public void Resume()
        {
            if (!_isPaused) return;
            _isPaused = false;
            _waveOut?.Play();
        }

        public void Stop()
        {
            if (_stopping) return;
            _stopping = true;

            try { _cts?.Cancel(); } catch { }

            if (_videoProcess != null) _videoProcess.Exited -= (_, _) => { };
            if (_audioProcess != null) _audioProcess.Exited -= (_, _) => { };

            CleanupAudio();

            try
            {
                if (_videoProcess != null && !_videoProcess.HasExited)
                    _videoProcess.Kill();
            }
            catch (Exception ex) { LogCallback?.Invoke($"Stop video error: {ex.Message}"); }
            finally
            {
                _videoProcess?.Dispose();
                _videoProcess = null;
            }

            _cts?.Dispose();
            _cts = null;
            _frameChannel = null;
            _isPaused = false;
            _videoEnded = true;
            _audioEnded = true;
            _stopping = false;
        }

        private void CleanupAudio()
        {
            try
            {
                _waveOut?.Stop();
                _waveOut?.Dispose();
            }
            catch { }
            _waveOut = null;
            _waveProvider = null;

            try
            {
                if (_audioProcess != null && !_audioProcess.HasExited)
                    _audioProcess.Kill();
            }
            catch { }
            finally
            {
                _audioProcess?.Dispose();
                _audioProcess = null;
            }
        }

        public TimeSpan GetCurrentPosition() => _lastPosition;
        public void Dispose() => Stop();
    }
}