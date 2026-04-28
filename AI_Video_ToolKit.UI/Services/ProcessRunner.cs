using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AI_Video_ToolKit.UI.Services
{
    public class ProcessRunner
    {
        private const string FFmpegPath = @"C:\_Portable_\ffmpeg\bin\ffmpeg.exe";

        public event Action<string>? OnOutput;
        public event Action<string>? OnError;

        public async Task Run(string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = FFmpegPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = new Process { StartInfo = psi };

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    OnOutput?.Invoke(e.Data);
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    OnError?.Invoke(e.Data);
            };

            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();
        }
    }
}