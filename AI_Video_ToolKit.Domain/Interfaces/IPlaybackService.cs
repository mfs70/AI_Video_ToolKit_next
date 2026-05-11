namespace AI_Video_ToolKit_next.Domain.Interfaces;

public interface IPlaybackService
{
    bool IsPlaying { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    double SpeedRatio { get; set; }

    event Action<TimeSpan>? PositionChanged;
    event Action<bool>? PlaybackStateChanged;

    void Load(string filePath);
    void Play();
    void Pause();
    void Stop();
    void Seek(TimeSpan position);
    void StepForward(int frames = 1);
    void StepBackward(int frames = 1);
}