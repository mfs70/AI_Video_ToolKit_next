// Файл: Messages/MediaMessages.cs
using System;
using System.Collections.Generic;
using AI_Video_ToolKit.UI.ViewModels;

namespace AI_Video_ToolKit.UI.Messages
{
    public record LoadFileMessage(string FilePath);
    public record FileLoadedMessage(string FilePath, double Duration, double Fps, bool HasAudio, long VideoBitrate);
    public record ImageLoadedMessage(string FilePath);
    public record LogMessage(string Text);
    public record MarkersChangedMessage(TimeSpan? Input, TimeSpan? Output, IReadOnlyList<TimeSpan> Cuts);
    // Сообщение о начале экспорта
    public record ExportStartedMessage();
    // Сообщение о прогрессе экспорта (0-100)
    public record ExportProgressMessage(int Percent);
    // Сообщение о завершении экспорта (успех/ошибка)
    public record ExportFinishedMessage(bool Success, string ResultPath = "");
    // Сообщение об отмене экспорта
    public record ExportCancelledMessage();
    // Сообщение об обновлении сегментов
    public record SegmentsChangedMessage(IReadOnlyList<SegmentInfo> Segments);
    //    public record SegmentsChangedMessage(IReadOnlyList<AI_Video_ToolKit.UI.ViewModels.SegmentInfo> Segments);
}