// Файл: D:\AI_Video_ToolKit_next\AI_Video_ToolKit.UI\Services\BufferedVideoPlayer.cs

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AI_Video_ToolKit.Infrastructure.Services;
using NAudio.Wave;

namespace AI_Video_ToolKit.UI.Services
{
    /// <summary>
    /// Буферизированный видеоплеер production-grade уровня.
    ///
    /// Основные улучшения:
    /// - Безопасный lifecycle задач
    /// - Безопасный shutdown
    /// - stderr draining для FFmpeg
    /// - Thread-safe stopping
    /// - Защита от race conditions
    /// - Защита от утечек ArrayPool
    /// - Подготовка к frame metadata
    /// - Улучшенная синхронизация playback
    /// - Улучшенная диагностика
    /// </summary>
    public sealed class BufferedVideoPlayer : IDisposable
    {
        #region Nested Types

        private sealed class VideoFrame
        {
            public byte[] Buffer = Array.Empty<byte>();
            public long FrameIndex;
            public TimeSpan PresentationTime;
        }

        private enum PlaybackState
        {
            Stopped,
            Starting,
            Playing,
            Paused,
            Stopping
        }

        #endregion

        #region Fields

        private readonly FFmpegProcessService _processService;

        private Process? _videoProcess;
        private Process? _audioProcess;

        private Task? _decodeTask;
        private Task? _playbackTask;
        private Task? _audioTask;
        private Task? _videoStderrTask;
        private Task? _audioStderrTask;

        private CancellationTokenSource? _cts;

        private int _width;
        private int _height;
        private int _stride;
        private int _frameSize;

        private double _fps;
        private double _speed = 1.0;

        private Channel<VideoFrame>? _frameChannel;

        private long _presentedFrames;
        private long _decodedFrames;
        private long _droppedFrames;

        private TimeSpan _startTime;
        private TimeSpan _lastPosition;

        private Stopwatch? _playbackClock;
        private TimeSpan _playbackClockOffset;

        private WaveOutEvent? _waveOut;
        private BufferedWaveProvider? _waveProvider;

        private volatile PlaybackState _state = PlaybackState.Stopped;

        private readonly object _syncRoot = new();

        private int _bufferedFrameCapacity;

        private WriteableBitmap? _writeableBitmap;

        #endregion

        #region Events

        public event Action<BitmapSource>? OnFrame;
        public event Action<TimeSpan>? OnPositionChanged;
        public event Action? OnPlaybackEnded;

        public Action<string>? LogCallback;

        #endregion

        #region Constructor

        public BufferedVideoPlayer(FFmpegProcessService processService)
        {
            _processService = processService;
        }

        #endregion

        #region Public API

        public void Start(
            string file,
            int width,
            int height,
            double fps,
            TimeSpan start,
            double speed = 1.0,
            bool enableAudio = true)
        {
            lock (_syncRoot)
            {
                try
                {
                    StopInternal(waitForShutdown: true);

                    _state = PlaybackState.Starting;

                    _width = Math.Max(16, width);
                    _height = Math.Max(16, height);
                    _stride = _width * 3;
                    _frameSize = _stride * _height;

                    _fps = fps > 0 ? fps : 25.0;
                    _speed = speed > 0 ? speed : 1.0;

                    _startTime = start;
                    _lastPosition = start;

                    _presentedFrames = 0;
                    _decodedFrames = 0;
                    _droppedFrames = 0;

                    _playbackClock = null;
                    _playbackClockOffset = TimeSpan.Zero;

                    _bufferedFrameCapacity = CalculateBufferCapacity(_width, _height);

                    _frameChannel = Channel.CreateBounded<VideoFrame>(
                        new BoundedChannelOptions(_bufferedFrameCapacity)
                        {
                            FullMode = BoundedChannelFullMode.DropOldest,
                            SingleReader = true,
                            SingleWriter = true,
                            AllowSynchronousContinuations = false
                        });

                    _cts = new CancellationTokenSource();

                    CreateWriteableBitmap();

                    StartVideoFFmpeg(file, start);

                    if (enableAudio)
                    {
                        StartAudioFFmpeg(file, start);
                    }

                    _decodeTask = Task.Run(() => DecodeLoop(_cts.Token));
                    _playbackTask = Task.Run(() => PlaybackLoop(_cts.Token));

                    _state = PlaybackState.Playing;

                    LogCallback?.Invoke("BufferedVideoPlayer started successfully.");
                }
                catch (Exception ex)
                {
                    LogCallback?.Invoke($"Start error: {ex}");
                    StopInternal(waitForShutdown: true);
                }
            }
        }

        public void Pause()
        {
            if (_state != PlaybackState.Playing)
            {
                return;
            }

            _state = PlaybackState.Paused;

            if (_playbackClock != null)
            {
                _playbackClockOffset += _playbackClock.Elapsed;
                _playbackClock.Stop();
            }

            _waveOut?.Pause();

            LogCallback?.Invoke("Playback paused.");
        }

        public void Resume()
        {
            if (_state != PlaybackState.Paused)
            {
                return;
            }

            _state = PlaybackState.Playing;

            if (_playbackClock != null)
            {
                _playbackClock.Start();
            }

            _waveOut?.Play();

            LogCallback?.Invoke("Playback resumed.");
        }

        public void Stop()
        {
            lock (_syncRoot)
            {
                StopInternal(waitForShutdown: true);
            }
        }

        public TimeSpan GetCurrentPosition()
        {
            return _lastPosition;
        }

        public void Dispose()
        {
            Stop();
        }

        #endregion

        #region Initialization

        private void CreateWriteableBitmap()
        {
            _writeableBitmap = new WriteableBitmap(
                _width,
                _height,
                96,
                96,
                PixelFormats.Bgr24,
                null);

            _writeableBitmap.Freeze();
        }

        private static int CalculateBufferCapacity(int width, int height)
        {
            long bytesPerFrame = (long)width * height * 3;

            if (bytesPerFrame <= 0)
            {
                return 12;
            }

            long targetBudget = 180L * 1024 * 1024;

            int cap = (int)(targetBudget / bytesPerFrame);

            return Math.Clamp(cap, 4, 48);
        }

        #endregion

        #region FFmpeg Startup

        private void StartVideoFFmpeg(string file, TimeSpan start)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _processService.FfmpegPath,
                Arguments =
                    $"-hide_banner " +
                    $"-loglevel warning " +
                    $"-hwaccel auto " +
                    $"-ss {start.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                    $"-i \"{file}\" " +
                    $"-vf scale={_width}:{_height}:force_original_aspect_ratio=decrease," +
                    $"pad={_width}:{_height}:(ow-iw)/2:(oh-ih)/2 " +
                    $"-pix_fmt bgr24 " +
                    $"-f rawvideo -",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _videoProcess = Process.Start(psi);

            if (_videoProcess == null)
            {
                throw new InvalidOperationException("Unable to start FFmpeg video process.");
            }

            _videoStderrTask = Task.Run(() => DrainStandardError(
                _videoProcess,
                "VIDEO",
                _cts!.Token));

            LogCallback?.Invoke("FFmpeg video process started.");
        }

        private void StartAudioFFmpeg(string file, TimeSpan start)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _processService.FfmpegPath,
                Arguments =
                    $"-hide_banner " +
                    $"-loglevel warning " +
                    $"-ss {start.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                    $"-i \"{file}\" " +
                    BuildAudioTempoArguments(_speed) +
                    $"-f s16le " +
                    $"-acodec pcm_s16le " +
                    $"-ar 44100 " +
                    $"-ac 2 -",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _audioProcess = Process.Start(psi);

            if (_audioProcess == null)
            {
                throw new InvalidOperationException("Unable to start FFmpeg audio process.");
            }

            _audioStderrTask = Task.Run(() => DrainStandardError(
                _audioProcess,
                "AUDIO",
                _cts!.Token));

            var waveFormat = new WaveFormat(44100, 16, 2);

            _waveProvider = new BufferedWaveProvider(waveFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(400),
                DiscardOnBufferOverflow = true,
                ReadFully = false
            };

            _waveOut = new WaveOutEvent
            {
                DesiredLatency = 80,
                NumberOfBuffers = 3
            };

            _waveOut.Init(_waveProvider);

            _audioTask = Task.Run(() => AudioReadLoop(_cts!.Token));

            LogCallback?.Invoke("Audio initialized.");
        }

        #endregion

        #region Decode Loop

        private async Task DecodeLoop(CancellationToken token)
        {
            if (_videoProcess == null || _frameChannel == null)
            {
                return;
            }

            Stream stream = _videoProcess.StandardOutput.BaseStream;
            ChannelWriter<VideoFrame> writer = _frameChannel.Writer;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(_frameSize);

                    bool success = false;

                    try
                    {
                        int read = 0;

                        while (read < _frameSize)
                        {
                            int r = await stream.ReadAsync(
                                buffer,
                                read,
                                _frameSize - read,
                                token);

                            if (r == 0)
                            {
                                writer.TryComplete();
                                return;
                            }

                            read += r;
                        }

                        long frameIndex = Interlocked.Increment(ref _decodedFrames);

                        var frame = new VideoFrame
                        {
                            Buffer = buffer,
                            FrameIndex = frameIndex,
                            PresentationTime = TimeSpan.FromSeconds(frameIndex / _fps)
                        };

                        await writer.WriteAsync(frame, token);

                        success = true;
                    }
                    finally
                    {
                        if (!success)
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                writer.TryComplete();
            }
            catch (Exception ex)
            {
                writer.TryComplete(ex);
                LogCallback?.Invoke($"DecodeLoop error: {ex}");
            }
        }

        #endregion

        #region Playback Loop

        private async Task PlaybackLoop(CancellationToken token)
        {
            if (_frameChannel == null)
            {
                return;
            }

            ChannelReader<VideoFrame> reader = _frameChannel.Reader;

            bool playbackCompletedNaturally = false;

            try
            {
                while (await reader.WaitToReadAsync(token))
                {
                    while (reader.TryRead(out VideoFrame? frame))
                    {
                        if (frame == null)
                        {
                            continue;
                        }

                        try
                        {
                            while (_state == PlaybackState.Paused && !token.IsCancellationRequested)
                            {
                                await Task.Delay(15, token);
                            }

                            if (token.IsCancellationRequested)
                            {
                                return;
                            }

                            if (_playbackClock == null)
                            {
                                _playbackClock = Stopwatch.StartNew();
                                _waveOut?.Play();
                            }

                            RenderFrame(frame.Buffer);

                            Interlocked.Increment(ref _presentedFrames);

                            TimeSpan clockElapsed = _playbackClockOffset + _playbackClock.Elapsed;

                            TimeSpan mediaElapsed =
                                TimeSpan.FromTicks((long)(clockElapsed.Ticks * _speed));

                            _lastPosition = _startTime + mediaElapsed;

                            SafeRaisePositionChanged(_lastPosition);

                            TimeSpan nextFrameMediaTime =
                                TimeSpan.FromSeconds(_presentedFrames / Math.Max(0.0001, _fps));

                            TimeSpan nextFrameClockTime =
                                TimeSpan.FromTicks((long)(nextFrameMediaTime.Ticks / _speed));

                            TimeSpan delay = nextFrameClockTime - clockElapsed;

                            if (delay > TimeSpan.Zero)
                            {
                                await Task.Delay(delay, token);
                            }
                            else if (delay < TimeSpan.FromMilliseconds(-35))
                            {
                                Interlocked.Increment(ref _droppedFrames);
                            }
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(frame.Buffer);
                        }
                    }
                }

                playbackCompletedNaturally = !token.IsCancellationRequested;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogCallback?.Invoke($"PlaybackLoop error: {ex}");
            }
            finally
            {
                if (playbackCompletedNaturally)
                {
                    FinishPlaybackNaturally();
                }
            }
        }

        private void RenderFrame(byte[] frameBuffer)
        {
            BitmapSource bitmap = BitmapSource.Create(
                _width,
                _height,
                96,
                96,
                PixelFormats.Bgr24,
                null,
                frameBuffer,
                _stride);

            bitmap.Freeze();

            SafeRaiseFrame(bitmap);
        }

        #endregion

        #region Audio Loop

        private async Task AudioReadLoop(CancellationToken token)
        {
            if (_audioProcess == null || _waveProvider == null)
            {
                return;
            }

            byte[] buffer = new byte[16384];

            try
            {
                Stream stream = _audioProcess.StandardOutput.BaseStream;

                while (!token.IsCancellationRequested)
                {
                    if (_waveProvider.BufferedDuration.TotalMilliseconds > 350)
                    {
                        await Task.Delay(10, token);
                        continue;
                    }

                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, token);

                    if (read == 0)
                    {
                        break;
                    }

                    _waveProvider.AddSamples(buffer, 0, read);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogCallback?.Invoke($"AudioReadLoop error: {ex}");
            }
        }

        #endregion

        #region FFmpeg Helpers

        private static string BuildAudioTempoArguments(double speed)
        {
            if (Math.Abs(speed - 1.0) < 0.001)
            {
                return string.Empty;
            }

            var filters = new List<string>();

            double remaining = speed;

            while (remaining > 2.0)
            {
                filters.Add("atempo=2.0");
                remaining /= 2.0;
            }

            while (remaining < 0.5)
            {
                filters.Add("atempo=0.5");
                remaining /= 0.5;
            }

            filters.Add(
                $"atempo={remaining.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");

            return $"-filter:a \"{string.Join(",", filters)}\" ";
        }

        private async Task DrainStandardError(
            Process process,
            string prefix,
            CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && !process.HasExited)
                {
                    string? line = await process.StandardError.ReadLineAsync();

                    if (line == null)
                    {
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        LogCallback?.Invoke($"FFmpeg[{prefix}] {line}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogCallback?.Invoke($"stderr drain error [{prefix}]: {ex.Message}");
            }
        }

        #endregion

        #region Shutdown

        private void FinishPlaybackNaturally()
        {
            lock (_syncRoot)
            {
                if (_state == PlaybackState.Stopping ||
                    _state == PlaybackState.Stopped)
                {
                    return;
                }

                StopInternal(waitForShutdown: true);

                try
                {
                    OnPlaybackEnded?.Invoke();
                }
                catch (Exception ex)
                {
                    LogCallback?.Invoke($"OnPlaybackEnded error: {ex}");
                }
            }
        }

        private void StopInternal(bool waitForShutdown)
        {
            if (_state == PlaybackState.Stopped ||
                _state == PlaybackState.Stopping)
            {
                return;
            }

            _state = PlaybackState.Stopping;

            try
            {
                _cts?.Cancel();
            }
            catch
            {
            }

            try
            {
                _frameChannel?.Writer.TryComplete();
            }
            catch
            {
            }

            if (waitForShutdown)
            {
                WaitTask(_decodeTask);
                WaitTask(_playbackTask);
                WaitTask(_audioTask);
                WaitTask(_videoStderrTask);
                WaitTask(_audioStderrTask);
            }

            CleanupAudio();
            CleanupProcess(ref _videoProcess);
            CleanupProcess(ref _audioProcess);

            _frameChannel = null;

            try
            {
                _cts?.Dispose();
            }
            catch
            {
            }

            _cts = null;

            _decodeTask = null;
            _playbackTask = null;
            _audioTask = null;
            _videoStderrTask = null;
            _audioStderrTask = null;

            _playbackClock = null;
            _playbackClockOffset = TimeSpan.Zero;

            _state = PlaybackState.Stopped;

            LogStatistics();
        }

        private static void WaitTask(Task? task)
        {
            if (task == null)
            {
                return;
            }

            try
            {
                task.Wait(1500);
            }
            catch
            {
            }
        }

        private void CleanupAudio()
        {
            try
            {
                _waveOut?.Stop();
            }
            catch
            {
            }

            try
            {
                _waveOut?.Dispose();
            }
            catch
            {
            }

            _waveOut = null;
            _waveProvider = null;
        }

        private void CleanupProcess(ref Process? process)
        {
            if (process == null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    process.WaitForExit(1500);
                }
            }
            catch
            {
            }
            finally
            {
                try
                {
                    process.Dispose();
                }
                catch
                {
                }

                process = null;
            }
        }

        #endregion

        #region Diagnostics

        private void LogStatistics()
        {
            LogCallback?.Invoke(
                $"Playback statistics | " +
                $"Decoded: {_decodedFrames} | " +
                $"Presented: {_presentedFrames} | " +
                $"Dropped: {_droppedFrames}");
        }

        #endregion

        #region Safe Event Dispatch

        private void SafeRaiseFrame(BitmapSource bitmap)
        {
            try
            {
                if (Application.Current?.Dispatcher != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            OnFrame?.Invoke(bitmap);
                        }
                        catch (Exception ex)
                        {
                            LogCallback?.Invoke($"OnFrame callback error: {ex}");
                        }
                    }));
                }
                else
                {
                    OnFrame?.Invoke(bitmap);
                }
            }
            catch (Exception ex)
            {
                LogCallback?.Invoke($"SafeRaiseFrame error: {ex}");
            }
        }

        private void SafeRaisePositionChanged(TimeSpan position)
        {
            try
            {
                OnPositionChanged?.Invoke(position);
            }
            catch (Exception ex)
            {
                LogCallback?.Invoke($"OnPositionChanged error: {ex}");
            }
        }

        #endregion
    }
}