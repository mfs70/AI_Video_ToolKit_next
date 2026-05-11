using CommunityToolkit.Mvvm.ComponentModel;

namespace AI_Video_ToolKit_next.UI.Models;

public partial class TimelineSegment : ObservableObject
{
    [ObservableProperty]
    private string _sourceFilePath = string.Empty;

    [ObservableProperty]
    private TimeSpan _startTime;

    [ObservableProperty]
    private TimeSpan _endTime;

    [ObservableProperty]
    private TimeSpan _duration;

    [ObservableProperty]
    private int _trackIndex;
}