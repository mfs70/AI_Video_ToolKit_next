# ШАГ 1 — СОЗДАЕМ БАЗУ HOTKEY SYSTEM

На этом шаге мы:

- НЕ меняем существующие hotkeys;
- НЕ ломаем MainWindow;
- НЕ удаляем code-behind;
- НЕ трогаем playback.

Мы только создаем compile-safe базу.

---

# ВАЖНОЕ РЕШЕНИЕ ПО ПАПКЕ Input

Сейчас папка:

```text
AI_Video_ToolKit.UI/Input/
```

изначально использовалась для media files.

Чтобы НЕ смешивать:

- видео;
- картинки;
- input system;

мы НЕ будем использовать эту папку.

---

# НОВАЯ СТРУКТУРА

Создаем:

```text
AI_Video_ToolKit.UI/Hotkeys/
```

Это production-safe решение.

---

# СОЗДАТЬ ФАЙЛ

```text
AI_Video_ToolKit.UI/Hotkeys/InputAction.cs
```

---

# ПОЛНЫЙ ФАЙЛ InputAction.cs

```csharp
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
```

---

# СОЗДАТЬ ФАЙЛ

```text
AI_Video_ToolKit.UI/Hotkeys/KeyBindingMap.cs
```

---

# ПОЛНЫЙ ФАЙЛ KeyBindingMap.cs

```csharp
using System.Collections.Generic;
using System.Windows.Input;

namespace AI_Video_ToolKit.UI.Hotkeys
{
    // ------------------------------------------------------------
    // Централизованная карта hotkeys.
    //
    // ВАЖНО:
    //
    // Пока это только compile-safe registry.
    //
    // Мы пока НЕ подключаем:
    // - commands
    // - hooks
    // - MainWindow
    //
    // Сначала создаем стабильную структуру.
    // ------------------------------------------------------------
    public static class KeyBindingMap
    {
        // --------------------------------------------------------
        // Главная карта:
        //
        // KeyGesture -> InputAction
        // --------------------------------------------------------
        public static readonly Dictionary<KeyGesture, InputAction>
            Bindings = new()
            {
                {
                    new KeyGesture(Key.Space),
                    InputAction.PlayPause
                },

                {
                    new KeyGesture(Key.K),
                    InputAction.Stop
                },

                {
                    new KeyGesture(Key.Right),
                    InputAction.NextFrame
                },

                {
                    new KeyGesture(Key.Left),
                    InputAction.PrevFrame
                },

                {
                    new KeyGesture(Key.N),
                    InputAction.NextFile
                },

                {
                    new KeyGesture(Key.P),
                    InputAction.PrevFile
                },

                {
                    new KeyGesture(Key.A),
                    InputAction.ToggleAudio
                },

                {
                    new KeyGesture(Key.V),
                    InputAction.ToggleVideo
                },

                {
                    new KeyGesture(Key.R),
                    InputAction.ToggleLoop
                },

                {
                    new KeyGesture(Key.W),
                    InputAction.Preview
                },

                {
                    new KeyGesture(Key.M),
                    InputAction.Merge
                },

                {
                    new KeyGesture(Key.I),
                    InputAction.MarkerIn
                },

                {
                    new KeyGesture(Key.O),
                    InputAction.MarkerOut
                },

                {
                    new KeyGesture(Key.C),
                    InputAction.MarkerCut
                },

                {
                    new KeyGesture(
                        Key.Z,
                        ModifierKeys.Control),
                    InputAction.UndoMarker
                }
            };
    }
}
```

---

# СОЗДАТЬ ФАЙЛ

```text
AI_Video_ToolKit.UI/Hotkeys/HotkeyService.cs
```

---

# ПОЛНЫЙ ФАЙЛ HotkeyService.cs

```csharp
using System;
using System.Windows.Input;

namespace AI_Video_ToolKit.UI.Hotkeys
{
    // ------------------------------------------------------------
    // Центральный сервис hotkeys.
    //
    // ВАЖНО:
    //
    // На этом этапе:
    //
    // - сервис НЕ знает MainWindow
    // - сервис НЕ знает playback
    // - сервис НЕ знает timeline
    //
    // Это intentional.
    //
    // Мы строим compile-safe foundation.
    // ------------------------------------------------------------
    public sealed class HotkeyService
    {
        // --------------------------------------------------------
        // Событие:
        //
        // Сообщает что выполнено action.
        // --------------------------------------------------------
        public event Action<InputAction>? OnActionTriggered;

        // --------------------------------------------------------
        // Проверка keyboard input.
        // --------------------------------------------------------
        public bool TryHandle(KeyEventArgs e)
        {
            foreach (var binding in KeyBindingMap.Bindings)
            {
                if (binding.Key.Matches(null, e))
                {
                    OnActionTriggered?.Invoke(binding.Value);

                    e.Handled = true;

                    return true;
                }
            }

            return false;
        }
    }
}
```

---

# НА ЭТОМ ШАГЕ МЫ ЕЩЕ НЕ ДЕЛАЕМ

- удаление Window_PreviewKeyDown;
- перенос hotkeys;
- удаление старых bindings;
- playback integration.

---

# ЧТО ПРОВЕРИТЬ СЕЙЧАС

# ЧЕКЛИСТ

После создания файлов:

- [ ] проект собирается
- [ ] namespace AI_Video_ToolKit.UI.Hotkeys существует
- [ ] ошибок компиляции нет
- [ ] HotkeyService виден из MainWindow
- [ ] InputAction виден из MainWindow
- [ ] KeyBindingMap виден из MainWindow

---

# СЛЕДУЮЩИЙ ШАГ

Только после успешной сборки:

мы подключим:

```text
HotkeyService
```

к:

```text
MainWindow.xaml.cs
```

НО:

- пока БЕЗ удаления старых hotkeys;
- сначала parallel mode;
- потом migration.

