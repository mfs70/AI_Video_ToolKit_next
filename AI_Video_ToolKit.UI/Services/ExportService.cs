using System.Diagnostics;

namespace AI_Video_ToolKit.Infrastructure.Services
{
    public class ExportService
    {
        private const string FFmpeg = @"C:\_Portable_\ffmpeg\bin\ffmpeg.exe";

        public void Export(string input, string output, double start, double duration)
        {
            var psi = new ProcessStartInfo
            {
                FileName = FFmpeg,
                Arguments = $"-ss {start} -i \"{input}\" -t {duration} -c copy \"{output}\" -y",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process.Start(psi);
        }
    }
}