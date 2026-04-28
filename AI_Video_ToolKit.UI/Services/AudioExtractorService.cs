using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace AI_Video_ToolKit.UI.Services
{
    public class AudioExtractorService
    {
        private const string FFmpegPath = @"C:\_Portable_\ffmpeg\bin\ffmpeg.exe";

        public async Task<string> Extract(string inputFile)
        {
            if (!File.Exists(FFmpegPath))
                throw new Exception("FFmpeg не найден: " + FFmpegPath);

            string output = Path.Combine(Path.GetTempPath(), $"audio_{Guid.NewGuid()}.wav");

            var psi = new ProcessStartInfo
            {
                FileName = FFmpegPath,
                Arguments =
                    $"-y -i \"{inputFile}\" " +
                    "-vn -acodec pcm_s16le -ar 44100 -ac 2 " +
                    $"\"{output}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = Process.Start(psi)!;

            string error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (!File.Exists(output))
            {
                throw new Exception("FFmpeg не создал аудио:\n" + error);
            }

            return output;
        }
    }
}