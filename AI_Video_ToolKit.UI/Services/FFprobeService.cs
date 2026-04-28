using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;

namespace AI_Video_ToolKit.UI.Services
{
    public class FFprobeService
    {
        private const string FFprobePath = @"C:\_Portable_\ffmpeg\bin\ffprobe.exe";

        public async Task<(int width, int height, double duration, double fps, bool hasAudio)> GetInfo(string file)
        {
            var psi = new ProcessStartInfo
            {
                FileName = FFprobePath,
                Arguments =
                    "-v error -select_streams v:0 " +
                    "-show_entries stream=width,height,r_frame_rate " +
                    "-show_entries format=duration " +
                    "-of default=noprint_wrappers=1 " +
                    $"\"{file}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var psiAudio = new ProcessStartInfo
            {
                FileName = FFprobePath,
                Arguments =
                    "-v error -select_streams a " +
                    "-show_entries stream=index " +
                    "-of csv=p=0 " +
                    $"\"{file}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = Process.Start(psi)!;
            string output = await process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();

            var processAudio = Process.Start(psiAudio)!;
            string audioOutput = await processAudio.StandardOutput.ReadToEndAsync();
            processAudio.WaitForExit();

            bool hasAudio = !string.IsNullOrWhiteSpace(audioOutput);

            int width = 640;
            int height = 360;
            double duration = 0;
            double fps = 30;

            foreach (var line in output.Split('\n'))
            {
                if (line.StartsWith("width="))
                    width = int.Parse(line.Replace("width=", ""));

                if (line.StartsWith("height="))
                    height = int.Parse(line.Replace("height=", ""));

                if (line.StartsWith("duration="))
                    double.TryParse(line.Replace("duration=", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out duration);

                if (line.StartsWith("r_frame_rate="))
                {
                    var val = line.Replace("r_frame_rate=", "");
                    var parts = val.Split('/');

                    if (parts.Length == 2 &&
                        double.TryParse(parts[0], out var num) &&
                        double.TryParse(parts[1], out var den) &&
                        den != 0)
                    {
                        fps = num / den;
                    }
                }
            }

            return (width, height, duration, fps, hasAudio);
        }
    }
}