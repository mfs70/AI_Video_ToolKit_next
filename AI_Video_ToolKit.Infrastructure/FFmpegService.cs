using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace AI_Video_ToolKit.Infrastructure
{
    public class FFmpegService
    {
        private readonly string _ffmpegPath;

        public FFmpegService(string ffmpegPath)
        {
            _ffmpegPath = ffmpegPath;
        }

        public (bool success, string log) Run(string args)
        {
            if (!File.Exists(_ffmpegPath))
            {
                return (false, $"FFmpeg NOT FOUND: {_ffmpegPath}");
            }

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

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                return (false, $"PROCESS START ERROR: {ex.Message}");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            process.WaitForExit();

            return (process.ExitCode == 0, log.ToString());
        }

        public (bool, string) Trim(string input, string output, string start, string duration)
        {
            if (!File.Exists(input))
                return (false, $"INPUT NOT FOUND: {input}");

            return Run($"-y -i \"{input}\" -ss {start} -t {duration} -c copy \"{output}\"");
        }

        public (bool, string) Split(string input, string pattern, int seconds)
        {
            if (!File.Exists(input))
                return (false, $"INPUT NOT FOUND: {input}");

            return Run($"-y -i \"{input}\" -f segment -segment_time {seconds} -c copy \"{pattern}\"");
        }

        public (bool, string) Crop(string input, string output, int w, int h, int x, int y)
        {
            if (!File.Exists(input))
                return (false, $"INPUT NOT FOUND: {input}");

            return Run($"-y -i \"{input}\" -vf \"crop={w}:{h}:{x}:{y}\" \"{output}\"");
        }

        // ===== FFPROBE =====
        public (bool, string) Probe(string input)
        {
            string ffprobe = _ffmpegPath.Replace("ffmpeg.exe", "ffprobe.exe");

            if (!File.Exists(ffprobe))
                return (false, $"FFPROBE NOT FOUND: {ffprobe}");

            return RunProbe(ffprobe, $"-v error -show_format -show_streams \"{input}\"");
        }

        private (bool, string) RunProbe(string exe, string args)
        {
            var process = new Process();

            process.StartInfo.FileName = exe;
            process.StartInfo.Arguments = args;

            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;

            var log = new StringBuilder();

            process.Start();

            log.Append(process.StandardOutput.ReadToEnd());
            log.Append(process.StandardError.ReadToEnd());

            process.WaitForExit();

            return (process.ExitCode == 0, log.ToString());
        }
    }
}