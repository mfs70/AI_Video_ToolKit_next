namespace AI_Video_ToolKit.Domain
{
    // RU: Модель описывает параметры видео
    // EN: Video metadata model
    public class VideoInfo
    {
        public int Width { get; set; }      // ширина видео
        public int Height { get; set; }     // высота видео
        public double FPS { get; set; }     // частота кадров

        public string VideoCodec { get; set; } = "";
    }
}