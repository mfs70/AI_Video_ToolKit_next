// Файл: AI_Video_ToolKit.Domain/MediaInfo.cs
namespace AI_Video_ToolKit.Domain
{
    /// <summary>
    /// Полная информация о медиафайле (видео + аудио).
    /// </summary>
    public class MediaInfo
    {
        public double Duration { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double Fps { get; set; }
        public string VideoCodec { get; set; } = string.Empty;
        public long VideoBitrate { get; set; }
        public bool HasAudio { get; set; }
        public string AudioCodec { get; set; } = string.Empty;
        public int AudioSampleRate { get; set; }
        public int AudioChannels { get; set; }
        public long AudioBitrate { get; set; }
    }
}