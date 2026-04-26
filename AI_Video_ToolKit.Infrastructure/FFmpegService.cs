using System;
using System.Diagnostics;
using System.Text;

namespace AI_Video_ToolKit.Infrastructure
{
    /// <summary>
    /// Сервис работы с FFmpeg
    /// Запускает процесс и возвращает лог + статус выполнения
    /// </summary>
    public class FFmpegService
    {
        private readonly string _ffmpegPath;

        public FFmpegService(string ffmpegPath)
        {
            _ffmpegPath = ffmpegPath;
        }

        /// <summary>
        /// Универсальный запуск FFmpeg
        /// </summary>
        public (bool success, string log) Run(string args)
        {
            var process = new Process();

            process.StartInfo.FileName = _ffmpegPath;
            process.StartInfo.Arguments = args;

            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            var log = new StringBuilder();

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    log.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    log.AppendLine(e.Data);
            };

            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            process.WaitForExit();

            return (process.ExitCode == 0, log.ToString());
        }

        // ================= ОПЕРАЦИИ =================

        public (bool, string) Trim(string input, string output, string start, string duration)
        {
            string args = $"-y -i \"{input}\" -ss {start} -t {duration} -c copy \"{output}\"";
            return Run(args);
        }

        public (bool, string) Split(string input, string pattern, int seconds)
        {
            string args = $"-y -i \"{input}\" -map 0 -c copy -f segment -segment_time {seconds} \"{pattern}\"";
            return Run(args);
        }

        public (bool, string) Crop(string input, string output, int w, int h, int x, int y)
        {
            string args = $"-y -i \"{input}\" -vf \"crop={w}:{h}:{x}:{y}\" \"{output}\"";
            return Run(args);
        }
    }
}