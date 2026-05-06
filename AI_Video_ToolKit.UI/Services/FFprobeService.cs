using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AI_Video_ToolKit.UI.Services
{
    /// <summary>
    /// Сервис для получения информации о видео- и аудиофайлах через ffprobe
    /// </summary>
    public class FFprobeService
    {
        // Путь к ffprobe.exe – при необходимости измените
        private const string FFprobePath = @"C:\_Portable_\ffmpeg\bin\ffprobe.exe";

        /// <summary>
        /// Получить всю доступную информацию о файле
        /// </summary>
        public async Task<MediaInfo> GetInfo(string filePath)
        {
            // Запрашиваем видео- и аудио-параметры в удобном формате
            string args = $"-v error -select_streams v:0 -show_entries stream=width,height,r_frame_rate,codec_name,bit_rate -of default=noprint_wrappers=1 \"{filePath}\"";
            string videoOutput = await RunFFprobe(args);

            args = $"-v error -select_streams a:0 -show_entries stream=codec_name,sample_rate,channels,bit_rate -of default=noprint_wrappers=1 \"{filePath}\"";
            string audioOutput = await RunFFprobe(args);

            args = $"-v error -show_entries format=duration -of default=noprint_wrappers=1 \"{filePath}\"";
            string durationOutput = await RunFFprobe(args);

            var info = new MediaInfo();

            // Парсим видео
            var dict = ParseOutput(videoOutput);
            info.width = GetInt(dict, "width");
            info.height = GetInt(dict, "height");
            info.codec = GetString(dict, "codec_name");
            info.videoBitrate = GetLong(dict, "bit_rate");
            string fpsStr = GetString(dict, "r_frame_rate");
            if (!string.IsNullOrEmpty(fpsStr))
            {
                var parts = fpsStr.Split('/');
                if (parts.Length == 2 && double.TryParse(parts[0], out double num) && double.TryParse(parts[1], out double den) && den != 0)
                    info.fps = num / den;
                else if (double.TryParse(fpsStr, out double fps))
                    info.fps = fps;
                else
                    info.fps = 25.0;
            }
            else info.fps = 25.0;

            // Парсим аудио
            var audioDict = ParseOutput(audioOutput);
            info.audioCodec = GetString(audioDict, "codec_name");
            info.audioSampleRate = GetInt(audioDict, "sample_rate");
            info.audioChannels = GetInt(audioDict, "channels");
            info.audioBitrate = GetLong(audioDict, "bit_rate");
            info.hasAudio = !string.IsNullOrEmpty(info.audioCodec);

            // Длительность
            var durDict = ParseOutput(durationOutput);
            string durStr = GetString(durDict, "duration");
            if (!string.IsNullOrEmpty(durStr) && double.TryParse(durStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double dur))
                info.duration = dur;
            else
                info.duration = 0;

            return info;
        }

        private async Task<string> RunFFprobe(string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = FFprobePath,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };
            using var process = Process.Start(psi);
            if (process == null) return "";
            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output;
        }

        private Dictionary<string, string> ParseOutput(string output)
        {
            var dict = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(output)) return dict;
            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('=');
                if (parts.Length == 2)
                    dict[parts[0].Trim()] = parts[1].Trim();
            }
            return dict;
        }

        private int GetInt(Dictionary<string, string> dict, string key) =>
            dict.ContainsKey(key) && int.TryParse(dict[key], out int val) ? val : 0;
        private long GetLong(Dictionary<string, string> dict, string key) =>
            dict.ContainsKey(key) && long.TryParse(dict[key], out long val) ? val : 0;
        private string GetString(Dictionary<string, string> dict, string key) =>
            dict.ContainsKey(key) ? dict[key] : "";
    }

    /// <summary>
    /// Класс для хранения всей информации о медиафайле
    /// </summary>
    public class MediaInfo
    {
        public double duration;
        public int width, height;
        public double fps;
        public string codec = "";
        public long videoBitrate;
        public bool hasAudio;
        public string audioCodec = "";
        public int audioSampleRate;
        public int audioChannels;
        public long audioBitrate;
    }
}