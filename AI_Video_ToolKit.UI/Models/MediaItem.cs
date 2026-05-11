using CommunityToolkit.Mvvm.ComponentModel;

namespace AI_Video_ToolKit_next.UI.Models;

public partial class MediaItem : ObservableObject
{
    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private TimeSpan _duration;

    // Добавь другие поля при необходимости (например, ThumbnailPath)
}