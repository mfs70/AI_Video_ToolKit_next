// Файл: AI_Video_ToolKit.Infrastructure/Services/FFmpegProcessService.cs
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AI_Video_ToolKit.Infrastructure.Services
{
    /// <summary>
    /// Асинхронный сервис для запуска ffmpeg/ffprobe.
    /// Возвращает код возврата и захватывает весь вывод stderr.
    /// </summary>
    public class FFmpegProcessService
    {
        public string FfmpegPath { get; }
        public string FfprobePath { get; }

        public FFmpegProcessService(string ffmpegPath, string ffprobePath)
        {
            FfmpegPath = ResolveToolPath(ffmpegPath, "ffmpeg.exe");
            FfprobePath = ResolveToolPath(ffprobePath, "ffprobe.exe");
        }

        /// <summary>
        /// Запуск ffmpeg с заданными аргументами.
        /// Возвращает true, если процесс завершился успешно (ExitCode == 0).
        /// </summary>
        public async Task<bool> RunFfmpegAsync(string arguments)
        {
            return await RunProcessAsync(FfmpegPath, arguments);
        }

        /// <summary>
        /// Runs ffmpeg and parses "-progress pipe:1" output so long exports can
        /// report smooth progress instead of updating only after a segment ends.
        /// </summary>
        public async Task<bool> RunFfmpegAsync(
            string arguments,
            TimeSpan expectedDuration,
            IProgress<double>? progress,
            CancellationToken token)
        {
            return await RunProcessAsync(FfmpegPath, arguments, expectedDuration, progress, token);
        }

        /// <summary>
        /// Запуск ffprobe с заданными аргументами.
        /// Возвращает консольный вывод (stdout) процесса.
        /// </summary>
        public async Task<string> RunFfprobeAsync(string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = FfprobePath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            using var process = TryStartProcess(psi);
            if (process == null)
                return string.Empty;

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            string output = await outputTask;
            _ = await stderrTask;

            // stderr может содержать предупреждения, но не ошибки.
            // Мы не бросаем исключение, возвращаем stdout.
            return output;
        }

        private async Task<bool> RunProcessAsync(string fileName, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = TryStartProcess(psi);
            if (process == null)
                return false;

            // FFmpeg can write enough diagnostics to fill stderr/stdout buffers.
            // Reading both streams in parallel prevents the child process from hanging.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();
            _ = await stdoutTask;
            _ = await stderrTask;
            return process.ExitCode == 0;
        }

        private async Task<bool> RunProcessAsync(
            string fileName,
            string arguments,
            TimeSpan expectedDuration,
            IProgress<double>? progress,
            CancellationToken token)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = TryStartProcess(psi);
            if (process == null)
                return false;

            using var registration = token.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { }
            });

            var stderrTask = process.StandardError.ReadToEndAsync();
            var progressTask = ReadFfmpegProgressAsync(process, expectedDuration, progress, token);

            await process.WaitForExitAsync(token);
            await progressTask;
            _ = await stderrTask;

            progress?.Report(process.ExitCode == 0 ? 1 : 0);
            return process.ExitCode == 0;
        }

        private static async Task ReadFfmpegProgressAsync(
            Process process,
            TimeSpan expectedDuration,
            IProgress<double>? progress,
            CancellationToken token)
        {
            if (progress == null)
            {
                await process.StandardOutput.ReadToEndAsync(token);
                return;
            }

            var durationSeconds = Math.Max(0.001, expectedDuration.TotalSeconds);
            while (!process.StandardOutput.EndOfStream && !token.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(token);
                if (line == null)
                    break;

                var separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                var key = line[..separator];
                var value = line[(separator + 1)..];
                if (key == "progress" && value == "end")
                {
                    progress.Report(1);
                    continue;
                }

                var elapsed = ParseProgressTimestamp(key, value);
                if (!elapsed.HasValue)
                    continue;

                progress.Report(Math.Clamp(elapsed.Value.TotalSeconds / durationSeconds, 0, 1));
            }
        }

        private static TimeSpan? ParseProgressTimestamp(string key, string value)
        {
            if ((key == "out_time_us" || key == "out_time_ms") &&
                long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
            {
                return TimeSpan.FromMilliseconds(microseconds / 1000.0);
            }

            if (key == "out_time" &&
                TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var time))
            {
                return time;
            }

            return null;
        }

        private static Process? TryStartProcess(ProcessStartInfo startInfo)
        {
            try
            {
                return Process.Start(startInfo);
            }
            catch (Win32Exception)
            {
                return null;
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }

        private static string ResolveToolPath(string configuredPath, string executableName)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
                return configuredPath;

            var fromEnvironment = Environment.GetEnvironmentVariable(Path.GetFileNameWithoutExtension(executableName).ToUpperInvariant());
            if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment))
                return fromEnvironment;

            // Let Windows resolve the executable from PATH when the portable path is absent.
            return executableName;
        }
    }
}
