// Файл: AI_Video_ToolKit.Infrastructure/Services/FFmpegProcessService.cs
using System.Diagnostics;
using System.Threading.Tasks;

namespace AI_Video_ToolKit.Infrastructure.Services
{
    /// <summary>
    /// Асинхронный сервис для запуска ffmpeg/ffprobe.
    /// Возвращает код возврата и захватывает весь вывод stderr.
    /// </summary>
    public class FFmpegProcessService
    {
        private readonly string _ffmpegPath;
        private readonly string _ffprobePath;

        public FFmpegProcessService(string ffmpegPath, string ffprobePath)
        {
            _ffmpegPath = ffmpegPath;
            _ffprobePath = ffprobePath;
        }

        /// <summary>
        /// Запуск ffmpeg с заданными аргументами.
        /// Возвращает true, если процесс завершился успешно (ExitCode == 0).
        /// </summary>
        public async Task<bool> RunFfmpegAsync(string arguments)
        {
            return await RunProcessAsync(_ffmpegPath, arguments);
        }

        /// <summary>
        /// Запуск ffprobe с заданными аргументами.
        /// Возвращает консольный вывод (stdout) процесса.
        /// </summary>
        public async Task<string> RunFfprobeAsync(string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffprobePath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process == null) return string.Empty;

            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

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

            using var process = Process.Start(psi);
            if (process == null) return false;

            // Асинхронно читаем stderr, чтобы не блокировать процесс.
            _ = Task.Run(async () =>
            {
                while (!process.StandardError.EndOfStream)
                    await process.StandardError.ReadLineAsync();
            });

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
    }
}