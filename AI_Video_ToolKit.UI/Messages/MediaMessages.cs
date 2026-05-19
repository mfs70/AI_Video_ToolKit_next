namespace AI_Video_ToolKit.UI.Messages
{
    public record LoadFileMessage(string FilePath);
    public record FileLoadedMessage(string FilePath, double Duration, double Fps, bool HasAudio, long VideoBitrate);
    public record LogMessage(string Text);
}