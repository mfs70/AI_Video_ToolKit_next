// Файл: AI_Video_ToolKit.Infrastructure/Services/FFmpegProcessService.cs
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
