using System;
using System.Collections.Generic;

namespace AI_Video_ToolKit.UI.Messages
{
    // Это сообщение будет отправляться, когда выбран новый файл
    public record FileSelectedMessage(string FilePath);

    // Это сообщение – когда файл загружен и метаданные получены
    public record FileLoadedMessage(string FilePath, double Duration, double Fps, bool HasAudio);

    // Просто тестовое сообщение
    public record PingMessage(string Text);
}