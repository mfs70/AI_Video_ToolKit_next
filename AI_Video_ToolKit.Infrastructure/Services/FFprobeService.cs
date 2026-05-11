// Файл: AI_Video_ToolKit.Infrastructure/Services/FFprobeService.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using AI_Video_ToolKit.Domain;

namespace AI_Video_ToolKit.Infrastructure.Services
{
    /// <summary>
    /// Сервис получения метаданных медиафайла через ffprobe.
    /// </summary>
    public class FFprobeService
    {
        private readonly FFmpegProcessService _processService;

        public FFprobeService(FFmpegProcessService processService)
        {
            _processService = processService;
        }

        public async Task<MediaInfo> GetInfoAsync(string filePath)
        {
            string videoArgs = $"-v error -select_streams v:0 -show_entries stream=width,height,r_frame_rate,codec_name,bit_rate -of default=noprint_wrappers=1 \"{filePath}\"";
            string audioArgs = $"-v error -select_streams a:0 -show_entries stream=codec_name,sample_rate,channels,bit_rate -of default=noprint_wrappers=1 \"{filePath}\"";
            string durationArgs = $"-v error -show_entries format=duration -of default=noprint_wrappers=1 \"{filePath}\"";

            string videoOutput = await _processService.RunFfprobeAsync(videoArgs);
            string audioOutput = await _processService.RunFfprobeAsync(audioArgs);
            string durationOutput = await _processService.RunFfprobeAsync(durationArgs);

            var videoDict = ParseOutput(videoOutput);
            var audioDict = ParseOutput(audioOutput);
            var durDict = ParseOutput(durationOutput);

            var info = new MediaInfo
            {
                Width = GetInt(videoDict, "width"),
                Height = GetInt(videoDict, "height"),
                Fps = ParseFps(GetString(videoDict, "r_frame_rate")),
                VideoCodec = GetString(videoDict, "codec_name"),
                VideoBitrate = GetLong(videoDict, "bit_rate"),
                AudioCodec = GetString(audioDict, "codec_name"),
                AudioSampleRate = GetInt(audioDict, "sample_rate"),
                AudioChannels = GetInt(audioDict, "channels"),
                AudioBitrate = GetLong(audioDict, "bit_rate"),
                HasAudio = !string.IsNullOrEmpty(GetString(audioDict, "codec_name"))
            };

            string durStr = GetString(durDict, "duration");
            if (double.TryParse(durStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double dur))
                info.Duration = dur;

            return info;
        }

        private Dictionary<string, string> ParseOutput(string output)
        {
            var dict = new Dictionary<string, string>();
            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('=');
                if (parts.Length == 2)
                    dict[parts[0].Trim()] = parts[1].Trim();
            }
            return dict;
        }

        private double ParseFps(string fpsStr)
        {
            if (string.IsNullOrEmpty(fpsStr)) return 25.0;
            var parts = fpsStr.Split('/');
            if (parts.Length == 2 && double.TryParse(parts[0], out double num) && double.TryParse(parts[1], out double den) && den != 0)
                return num / den;
            if (double.TryParse(fpsStr, out double fps))
                return fps;
            return 25.0;
        }

        private int GetInt(Dictionary<string, string> dict, string key) =>
            dict.TryGetValue(key, out var val) && int.TryParse(val, out int i) ? i : 0;

        private long GetLong(Dictionary<string, string> dict, string key) =>
            dict.TryGetValue(key, out var val) && long.TryParse(val, out long l) ? l : 0;

        private string GetString(Dictionary<string, string> dict, string key) =>
            dict.TryGetValue(key, out var val) ? val : string.Empty;
    }
}