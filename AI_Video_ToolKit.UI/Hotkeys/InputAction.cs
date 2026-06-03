//  ПОЛНЫЙ ФАЙЛ InputAction.cs

namespace AI_Video_ToolKit.UI.Hotkeys
{
    // ------------------------------------------------------------
    // Все действия keyboard input системы.
    //
    // ВАЖНО:
    //
    // Здесь НЕ должно быть:
    // - WPF logic
    // - Key bindings
    // - UI controls
    //
    // Только перечисление действий.
    // ------------------------------------------------------------
    public enum InputAction
    {
        PlayPause,

        Stop,

        LoadFiles,

        IncreaseSpeed,

        DecreaseSpeed,

        Speed1,

        Speed2,

        Speed4,

        Speed8,

        NextFrame,

        PrevFrame,

        NextFile,

        PrevFile,

        ToggleAudio,

        ToggleVideo,

        ToggleLoop,

        ExportAll,

        Merge,

        Preview,

        MarkerIn,

        MarkerOut,

        MarkerCut,

        UndoMarker
    }
}
