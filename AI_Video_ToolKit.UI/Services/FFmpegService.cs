using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AI_Video_ToolKit.UI.Services
{
    /// <summary>
    /// Сервис для запуска внешних процессов (FFmpeg / FFprobe / FFplay)
    /// </summary>
    public class FFmpegService
    {
        // =====================================================
        // СОБЫТИЯ (events)
        // =====================================================

        // stdout
        public event Action<string>? OnOutput;

        // stderr (важно — тут FFmpeg пишет прогресс)
        public event Action<string>? OnError;

        // завершение процесса
        public event Action? OnCompleted;

        // =====================================================
        // ОСНОВНОЙ МЕТОД
        // =====================================================

        /// <summary>
        /// Асинхронный запуск процесса
        /// </summary>
        public async Task RunProcessAsync(string fileName, string arguments)
        {
            await Task.Run(() =>
            {
                var process = new Process();

                process.StartInfo.FileName = fileName;
                process.StartInfo.Arguments = arguments;

                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;

                // stdout
                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        OnOutput?.Invoke(e.Data);
                };

                // stderr
                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        OnError?.Invoke(e.Data);
                };

                process.Start();

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                process.WaitForExit();

                OnCompleted?.Invoke();
            });
        }
    }
}
