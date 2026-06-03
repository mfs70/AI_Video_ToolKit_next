
// AI_Video_ToolKit.UI/Hotkeys/KeyBindingMap.cs

using System.Collections.Generic;
using System.Windows.Input;

namespace AI_Video_ToolKit.UI.Hotkeys
{
    // ------------------------------------------------------------
    // Простое описание hotkey.
    //
    // НЕ используем WPF KeyGesture,
    // потому что WPF ломается на:
    //
    // - K
    // - I
    // - O
    // - C
    // - V
    // - A
    //
    // без modifier keys.
    // ------------------------------------------------------------
    public sealed class HotkeyBinding
    {
        public Key Key { get; init; }

        public ModifierKeys Modifiers { get; init; }

        public InputAction Action { get; init; }
    }

    // ------------------------------------------------------------
    // Централизованная карта hotkeys.
    // ------------------------------------------------------------
    public static class KeyBindingMap
    {
        public static readonly List<HotkeyBinding>
            Bindings = new()
            {
                new()
                {
                    Key = Key.Space,
                    Action = InputAction.PlayPause
                },

                new()
                {
                    Key = Key.K,
                    Action = InputAction.Stop
                },

                new()
                {
                    Key = Key.S,
                    Action = InputAction.Stop
                },

                new()
                {
                    Key = Key.L,
                    Modifiers = ModifierKeys.Control,
                    Action = InputAction.LoadFiles
                },

                new()
                {
                    Key = Key.L,
                    Action = InputAction.IncreaseSpeed
                },

                new()
                {
                    Key = Key.J,
                    Action = InputAction.DecreaseSpeed
                },

                new()
                {
                    Key = Key.D1,
                    Modifiers = ModifierKeys.Control,
                    Action = InputAction.Speed1
                },

                new()
                {
                    Key = Key.NumPad1,
                    Modifiers = ModifierKeys.Control,
                    Action = InputAction.Speed1
                },

                new()
                {
                    Key = Key.D2,
                    Modifiers = ModifierKeys.Control,
                    Action = InputAction.Speed2
                },

                new()
                {
                    Key = Key.NumPad2,
                    Modifiers = ModifierKeys.Control,
                    Action = InputAction.Speed2
                },

                new()
                {
                    Key = Key.D4,
                    Modifiers = ModifierKeys.Control,
                    Action = InputAction.Speed4
                },

                new()
                {
                    Key = Key.NumPad4,
                    Modifiers = ModifierKeys.Control,
                    Action = InputAction.Speed4
                },

                new()
                {
                    Key = Key.D8,
                    Modifiers = ModifierKeys.Control,
                    Action = InputAction.Speed8
                },

                new()
                {
                    Key = Key.NumPad8,
                    Modifiers = ModifierKeys.Control,
                    Action = InputAction.Speed8
                },

                new()
                {
                    Key = Key.Right,
                    Action = InputAction.NextFrame
                },

                new()
                {
                    Key = Key.Right,
                    Modifiers = ModifierKeys.Shift,
                    Action = InputAction.NextFrame
                },

                new()
                {
                    Key = Key.Left,
                    Action = InputAction.PrevFrame
                },

                new()
                {
                    Key = Key.Left,
                    Modifiers = ModifierKeys.Shift,
                    Action = InputAction.PrevFrame
                },

                new()
                {
                    Key = Key.N,
                    Action = InputAction.NextFile
                },

                new()
                {
                    Key = Key.P,
                    Action = InputAction.PrevFile
                },

                new()
                {
                    Key = Key.A,
                    Action = InputAction.ToggleAudio
                },

                new()
                {
                    Key = Key.V,
                    Action = InputAction.ToggleVideo
                },

                new()
                {
                    Key = Key.R,
                    Action = InputAction.ToggleLoop
                },

                new()
                {
                    Key = Key.W,
                    Action = InputAction.Preview
                },

                new()
                {
                    Key = Key.M,
                    Action = InputAction.Merge
                },

                new()
                {
                    Key = Key.I,
                    Action = InputAction.MarkerIn
                },

                new()
                {
                    Key = Key.O,
                    Action = InputAction.MarkerOut
                },

                new()
                {
                    Key = Key.C,
                    Action = InputAction.MarkerCut
                },

                new()
                {
                    Key = Key.Z,
                    Modifiers = ModifierKeys.Control,
                    Action = InputAction.UndoMarker
                }
            };
    }
}
