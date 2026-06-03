using System.Windows.Input;

namespace AI_Video_ToolKit.UI.Hotkeys
{
    public sealed class HotkeyService
    {
        public bool TryResolve(KeyEventArgs e, out InputAction action)
        {
            action = default;

            foreach (var binding in KeyBindingMap.Bindings)
            {
                if (binding.Key != e.Key)
                    continue;

                if (binding.Modifiers != Keyboard.Modifiers)
                    continue;

                action = binding.Action;
                return true;
            }

            return false;
        }
    }
}
