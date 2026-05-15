using System;
using System.Windows.Input;

namespace AI_Video_ToolKit.UI.Hotkeys
{
    // ------------------------------------------------------------
    // Центральный сервис hotkeys.
    //
    // ВАЖНО:
    //
    // Сейчас работает в parallel mode.
    //
    // Старые hotkeys еще НЕ удалены.
    // ------------------------------------------------------------
    public sealed class HotkeyService
    {
        // --------------------------------------------------------
        // Событие:
        //
        // сообщает что action выполнен.
        // --------------------------------------------------------
        public event Action<InputAction>? OnActionTriggered;

        // --------------------------------------------------------
        // Проверка keyboard input.
        // --------------------------------------------------------
        public bool TryHandle(KeyEventArgs e)
        {
            foreach (var binding in KeyBindingMap.Bindings)
            {
                // ------------------------------------------------
                // Проверяем:
                // - key
                // - modifiers
                // ------------------------------------------------
                if (binding.Key == e.Key &&
                    binding.Modifiers ==
                    Keyboard.Modifiers)
                {
                    OnActionTriggered?.Invoke(
                        binding.Action);

 //                   e.Handled = true;

                    return true;
                }
            }

            return false;
        }
    }
}