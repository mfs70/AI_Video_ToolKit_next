using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;

namespace AI_Video_ToolKit.UI.Services
{
    public class FFprobeService
    {
        private const string FFPROBE_PATH = @"C:\_Portable_\FFMPEG\bin\ffprobe.exe";

        public async Task<(int width, int height, double duration, double fps, bool hasAudio, string codec)> GetInfo(string file)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = FFPROBE_PATH,
                    Arguments = $"-v quiet -print_format json -show_streams -show_format \"{file}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                    return Default();

                string json = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (string.IsNullOrWhiteSpace(json))
                    return Default();

                using var doc = JsonDocument.Parse(json);

                int width = 0;
                int height = 0;
                double duration = 0;
                double fps = 25;
                bool hasAudio = false;
                string codec = "unknown";

                if (doc.RootElement.TryGetProperty("streams", out var streams))
                {
                    foreach (var stream in streams.EnumerateArray())
                    {
                        var type = stream.GetProperty("codec_type").GetString();

                        if (type == "video")
                        {
                            if (stream.TryGetProperty("width", out var w))
                                width = w.GetInt32();

                            if (stream.TryGetProperty("height", out var h))
                                height = h.GetInt32();

                            if (stream.TryGetProperty("codec_name", out var c))
                                codec = c.GetString() ?? "unknown";

                            if (stream.TryGetProperty("avg_frame_rate", out var fr))
                            {
                                var val = fr.GetString();

                                if (!string.IsNullOrEmpty(val) && val.Contains("/"))
                                {
                                    var parts = val.Split('/');
                                    if (double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var num) &&
                                        double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var den) &&
                                        den != 0)
                                    {
                                        fps = num / den;
                                    }
                                }
                            }
                        }

                        if (type == "audio")
                            hasAudio = true;
                    }
                }

                if (doc.RootElement.TryGetProperty("format", out var format))
                {
                    if (format.TryGetProperty("duration", out var dur))
                    {
                        var durStr = dur.GetString();

                        if (!string.IsNullOrEmpty(durStr))
                        {
                            if (!double.TryParse(durStr, NumberStyles.Any, CultureInfo.InvariantCulture, out duration))
                            {
                                // fallback для локали с запятой
                                durStr = durStr.Replace(',', '.');
                                double.TryParse(durStr, NumberStyles.Any, CultureInfo.InvariantCulture, out duration);
                            }
                        }
                    }
                }

                return (width, height, duration, fps, hasAudio, codec);
            }
            catch
            {
                return Default();
            }
        }

        private (int, int, double, double, bool, string) Default()
        {
            return (0, 0, 0, 25, false, "unknown");
        }
    }
}