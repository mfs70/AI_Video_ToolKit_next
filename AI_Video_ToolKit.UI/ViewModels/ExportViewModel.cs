// Файл: ViewModels/ExportViewModel.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using AI_Video_ToolKit.Infrastructure.Services;
using AI_Video_ToolKit.UI.Messages;
using AI_Video_ToolKit.UI.ViewModels;

namespace AI_Video_ToolKit.UI.ViewModels
{
    /// <summary>
    /// Управляет экспортом сегментов видео.
    /// </summary>
    public partial class ExportViewModel : ObservableObject, IDisposable
    {
        private readonly FFmpegProcessService _ffmpeg;
        private readonly IMessenger _messenger;
        private CancellationTokenSource? _cts;

        // Свойства для привязки к UI
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanExport))]
        private bool _isBusy;

        [ObservableProperty]
        private int _exportProgress;

        [ObservableProperty]
        private string _exportStatus = "✅ Готов";

        // Список сегментов для экспорта (обновляется через сообщение)
        private IReadOnlyList<SegmentInfo> _segments = Array.Empty<SegmentInfo>();
        private string _currentFilePath = string.Empty;
        private double _fps;
        private long _videoBitrate;

        // Можно ли экспортировать (не занят и есть сегменты)
        //public bool CanExport => !IsBusy && _segments.Any();
        public bool CanExport
        {
            get
            {
                var result = !IsBusy && _segments.Any();
                _messenger.Send(new Messages.PingMessage($"Fedor CanExport: IsBusy={IsBusy}, SegmentsCount={_segments.Count}, Result={result}"));
                System.Diagnostics.Debug.WriteLine($"CanExport: IsBusy={IsBusy}, SegmentsCount={_segments.Count}, Result={result}");
                return result;
            }
        }

        public ExportViewModel(FFmpegProcessService ffmpeg, IMessenger messenger)
        {
            _ffmpeg = ffmpeg;
            _messenger = messenger;

            // Подписываемся на обновление сегментов
            _messenger.Register<SegmentsChangedMessage>(this, (r, m) =>
            {
                _segments = m.Segments;
                OnPropertyChanged(nameof(CanExport));
            });

            // Подписываемся на информацию о текущем файле
            _messenger.Register<FileLoadedMessage>(this, (r, m) =>
            {
                _currentFilePath = m.FilePath;
                _fps = m.Fps;
                _videoBitrate = m.VideoBitrate;
            });
        }

        [RelayCommand(CanExecute = nameof(CanExport))]
        private async Task ExportSelected()
        {
            if (_segments.Count == 0) return;

            await ExportSegments(_segments);
        }

        [RelayCommand(CanExecute = nameof(CanExport))]
        private async Task ExportAll()
        {
            if (_segments.Count == 0) return;

            await ExportSegments(_segments);
        }

        [RelayCommand]
        private void CancelExport()
        {
            _cts?.Cancel();
            ExportStatus = "⏹ Отмена...";
            _messenger.Send(new ExportCancelledMessage());
        }

        private async Task ExportSegments(IReadOnlyList<SegmentInfo> segments)
        {
            if (IsBusy) return;
            if (string.IsNullOrEmpty(_currentFilePath))
            {
                ExportStatus = "❌ Нет загруженного файла";
                return;
            }

            IsBusy = true;
            ExportProgress = 0;
            _cts = new CancellationTokenSource();

            _messenger.Send(new ExportStartedMessage());

            var root = Directory.GetCurrentDirectory();
            var cutDir = Path.Combine(root, "Cut");
            Directory.CreateDirectory(cutDir);

            var srcName = Path.GetFileNameWithoutExtension(_currentFilePath);
            var ext = Path.GetExtension(_currentFilePath);
            var totalSegments = segments.Count;
            var successfulExports = 0;

            for (int i = 0; i < totalSegments; i++)
            {
                if (_cts.Token.IsCancellationRequested)
                {
                    ExportStatus = "⏹ Экспорт отменён";
                    _messenger.Send(new ExportFinishedMessage(false));
                    break;
                }

                var seg = segments[i];
                var outFile = Path.Combine(cutDir, $"{seg.Index:000}_{srcName}_{seg.StartFrame}_{seg.EndFrame}{ext}");

                ExportStatus = $"📤 Экспорт {i + 1} / {totalSegments}: {Path.GetFileName(outFile)}";

                var success = await ExportSingleSegment(seg, outFile, _cts.Token);
                if (success)
                    successfulExports++;

                ExportProgress = (int)((i + 1) * 100.0 / totalSegments);
            }

            IsBusy = false;

            if (successfulExports == totalSegments)
            {
                ExportStatus = $"✅ Экспорт завершён: {successfulExports} файлов";
                _messenger.Send(new ExportFinishedMessage(true, cutDir));
            }
            else if (successfulExports > 0)
            {
                ExportStatus = $"⚠ Экспорт частичный: {successfulExports} из {totalSegments}";
                _messenger.Send(new ExportFinishedMessage(true, cutDir));
            }
            else if (_cts.IsCancellationRequested)
            {
                ExportStatus = "⏹ Экспорт отменён";
                _messenger.Send(new ExportFinishedMessage(false));
            }
            else
            {
                ExportStatus = "❌ Ошибка экспорта";
                _messenger.Send(new ExportFinishedMessage(false));
            }

            _cts.Dispose();
            _cts = null;
            OnPropertyChanged(nameof(CanExport));
        }

        private async Task<bool> ExportSingleSegment(SegmentInfo seg, string outFile, CancellationToken token)
        {
            try
            {
                var startTime = seg.Start.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var endTime = seg.End.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var bitrateKbps = Math.Max(1500, (int)((_videoBitrate > 0 ? _videoBitrate : 4_000_000) / 1000));

                var args = $"-y -ss {startTime} -to {endTime} -i \"{_currentFilePath}\" " +
                          $"-c:v libx264 -preset veryfast -b:v {bitrateKbps}k " +
                          $"-c:a aac -ar 48000 -vsync cfr -async 1 -reset_timestamps 1 " +
                          $"-movflags +faststart \"{outFile}\"";

                var success = await _ffmpeg.RunFfmpegAsync(args);

                if (!success && File.Exists(outFile))
                    File.Delete(outFile);

                return success;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                _messenger.Send(new LogMessage($"Export error: {ex.Message}"));
                return false;
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _messenger.Unregister<SegmentsChangedMessage>(this);
            _messenger.Unregister<FileLoadedMessage>(this);
        }
    }
}