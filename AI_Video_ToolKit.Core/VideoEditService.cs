using AI_Video_ToolKit.Infrastructure;

namespace AI_Video_ToolKit.Core
{
    /// <summary>
    /// Бизнес-логика работы с видео
    /// </summary>
    public class VideoEditService
    {
        private readonly FFmpegService _ffmpeg;

        public VideoEditService(FFmpegService ffmpeg)
        {
            _ffmpeg = ffmpeg;
        }

        public (bool, string) Trim(string input, string output, string start, string duration)
            => _ffmpeg.Trim(input, output, start, duration);

        public (bool, string) Split(string input, string pattern, int seconds)
            => _ffmpeg.Split(input, pattern, seconds);

        public (bool, string) Crop(string input, string output, int w, int h, int x, int y)
            => _ffmpeg.Crop(input, output, w, h, x, y);
    }
}