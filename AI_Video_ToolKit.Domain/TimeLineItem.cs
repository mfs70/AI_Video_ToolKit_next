namespace AI_Video_ToolKit.Domain
{
    /// <summary>
    /// Один клип на таймлайне
    /// </summary>
    public class TimelineItem
    {
        public string FilePath { get; set; } = "";

        // позиция на таймлайне (в пикселях)
        public double X { get; set; }

        // длительность (в пикселях)
        public double Width { get; set; } = 120;

        public bool IsImage { get; set; }
    }
}