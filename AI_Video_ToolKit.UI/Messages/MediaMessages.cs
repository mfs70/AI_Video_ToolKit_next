// Файл: Messages/MediaMessages.cs
namespace AI_Video_ToolKit.UI.Messages
{
    public record LoadFileMessage(string FilePath);
    public record FileLoadedMessage(string FilePath, double Duration, double Fps, bool HasAudio, long VideoBitrate);
    public record ImageLoadedMessage(string FilePath);
    public record LogMessage(string Text);
    public record MarkersChangedMessage(TimeSpan? Input, TimeSpan? Output, IReadOnlyList<TimeSpan> Cuts);
}