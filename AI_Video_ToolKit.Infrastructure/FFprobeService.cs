using System.Diagnostics;
using System.Text.Json.Nodes;
using AI_Video_ToolKit.Domain;

namespace AI_Video_ToolKit.Infrastructure
{
    // RU: Получает параметры видео через ffprobe
    public class FFprobeService
    {
        private string _ffprobe;

        public FFprobeService(string path)
        {
            _ffprobe = path;
        }

        public VideoInfo GetInfo(string file)
        {
            var p = new Process();

            p.StartInfo.FileName = _ffprobe;
            p.StartInfo.Arguments = $"-v quiet -print_format json -show_streams \"{file}\"";
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.UseShellExecute = false;

            p.Start();

            string json = p.StandardOutput.ReadToEnd();
            p.WaitForExit();

            var root = JsonNode.Parse(json);
            var streams = root?["streams"]?.AsArray();

            var v = streams?.FirstOrDefault(s => s?["codec_type"]?.ToString() == "video");

            if (v == null) return new VideoInfo();

            return new VideoInfo
            {
                Width = int.Parse(v["width"]?.ToString() ?? "0"),
                Height = int.Parse(v["height"]?.ToString() ?? "0"),
                FPS = ParseFps(v["r_frame_rate"]?.ToString() ?? "0/1"),
                VideoCodec = v["codec_name"]?.ToString() ?? ""
            };
        }

        private double ParseFps(string val)
        {
            var p = val.Split('/');
            if (p.Length == 2 &&
                double.TryParse(p[0], out double a) &&
                double.TryParse(p[1], out double b) && b != 0)
                return a / b;

            return 0;
        }
    }
}
