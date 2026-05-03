using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace AI_Video_ToolKit.UI.Services
{
    public class FrameCacheService
    {
        private readonly Dictionary<string, BitmapImage> _cache = new();

        public async Task<BitmapImage> GetFrame(
            string file,
            TimeSpan time,
            Func<string, TimeSpan, Task<BitmapImage>> loader)
        {
            string key = $"{file}_{(int)time.TotalMilliseconds}";

            if (_cache.TryGetValue(key, out var cached))
                return cached;

            var frame = await loader(file, time);

            _cache[key] = frame;

            return frame;
        }

        public void Clear()
        {
            _cache.Clear();
        }
    }
}