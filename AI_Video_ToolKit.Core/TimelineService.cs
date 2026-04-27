using System.Collections.Generic;
using AI_Video_ToolKit.Domain;

namespace AI_Video_ToolKit.Core
{
    /// <summary>
    /// Управление таймлайном
    /// </summary>
    public class TimelineService
    {
        public List<TimelineItem> Items { get; } = new();

        public void Add(string file)
        {
            Items.Add(new TimelineItem
            {
                FilePath = file,
                X = Items.Count * 130,
                Width = 120,
                IsImage = file.EndsWith(".png") || file.EndsWith(".jpg")
            });
        }
    }
}