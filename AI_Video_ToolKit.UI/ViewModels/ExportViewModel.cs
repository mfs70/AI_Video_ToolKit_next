// Файл: ViewModels/ExportViewModel.cs
// Описание: ViewModel для экспорта сегментов видео. Управляет процессом экспорта,
// отображает прогресс и статус, поддерживает отмену.

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
        private readonly FFmpegProcessService _ffmpeg;  // Сервис для выполнения команд FFmpeg
        private readonly IMessenger _messenger;        // Шина сообщений для коммуникации
        private CancellationTokenSource? _cts;         // Токен отмены экспорта

        // Признак выполнения экспорта (true – идёт экспорт)
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanExport))]
        private bool _isBusy;

        // Прогресс экспорта в процентах (0–100)
        [ObservableProperty]
        private int _exportProgress;

        // Текстовый статус экспорта (отображается в UI)
        [ObservableProperty]
        private string _exportStatus = "✅ Готов";

        // Список сегментов для экспорта (обновляется через сообщение от MarkersViewModel)
        private IReadOnlyList<SegmentInfo> _segments = Array.Empty<SegmentInfo>();

        // Путь к текущему загруженному видеофайлу
        private string _currentFilePath = string.Empty;

        // Частота кадров текущего видео (кадров/сек)
        private double _fps;

        // Битрейт видео (бит/сек) для расчёта параметров экспорта
        private long _videoBitrate;

        /// <summary>
        /// Можно ли выполнить экспорт.
        /// true, если экспорт не активен и есть хотя бы один сегмент.
        /// </summary>
        public bool CanExport => !IsBusy && _segments.Any();

        /// <summary>
        /// Конструктор. Получает зависимости через DI.
        /// Подписывается на сообщения об изменении сегментов и загрузке файла.
        /// </summary>
        /// <param name="ffmpeg">Сервис для работы с FFmpeg</param>
        /// <param name="messenger">Шина сообщений</param>
        public ExportViewModel(FFmpegProcessService ffmpeg, IMessenger messenger)
        {
            _ffmpeg = ffmpeg;
            _messenger = messenger;

            // Подписка на обновление списка сегментов (приходит от MarkersViewModel)
            _messenger.Register<SegmentsChangedMessage>(this, (r, m) =>
            {
                _segments = m.Segments;
                OnPropertyChanged(nameof(CanExport));
            });

            // Подписка на информацию о загруженном видеофайле (приходит от PlayerViewModel)
            _messenger.Register<FileLoadedMessage>(this, (r, m) =>
            {
                _currentFilePath = m.FilePath;
                _fps = m.Fps;
                _videoBitrate = m.VideoBitrate;
            });
        }

        /// <summary>
        /// Команда экспорта выделенного сегмента.
        /// Вызывается из UI, передаётся выбранный сегмент.
        /// </summary>
        /// <param name="segment">Экспортируемый сегмент</param>
        [RelayCommand(CanExecute = nameof(CanExport))]
        private async Task ExportSelected(SegmentInfo? segment)
        {
            // Если сегмент не выбран – показываем сообщение и выходим
            if (segment == null)
            {
                ExportStatus = "❌ Выберите сегмент для экспорта";
                return;
            }

            // Экспортируем один сегмент, помещая его в список
            await ExportSegments(new List<SegmentInfo> { segment });
        }

        /// <summary>
        /// Команда экспорта всех сегментов.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExport))]
        private async Task ExportAll()
        {
            if (_segments.Count == 0)
            {
                ExportStatus = "❌ Нет сегментов для экспорта";
                return;
            }

            await ExportSegments(_segments);
        }

        /// <summary>
        /// Команда отмены текущего экспорта.
        /// </summary>
        [RelayCommand]
        private void CancelExport()
        {
            _cts?.Cancel();
            ExportStatus = "⏹ Отмена...";
            _messenger.Send(new ExportCancelledMessage());
        }

        /// <summary>
        /// Основной метод экспорта переданного списка сегментов.
        /// Отвечает за создание папки, формирование имён файлов,
        /// обновление прогресса и статуса.
        /// </summary>
        /// <param name="segments">Список сегментов для экспорта</param>
        private async Task ExportSegments(IReadOnlyList<SegmentInfo> segments)
        {
            // Защита от повторного запуска
            if (IsBusy) return;

            // Проверка, что файл загружен
            if (string.IsNullOrEmpty(_currentFilePath))
            {
                ExportStatus = "❌ Нет загруженного файла";
                return;
            }

            // Начинаем экспорт
            IsBusy = true;
            ExportProgress = 0;
            _cts = new CancellationTokenSource();

            // Сообщаем остальным о начале экспорта
            _messenger.Send(new ExportStartedMessage());

            // Создаём папку Cut в текущей директории, если её нет
            var root = Directory.GetCurrentDirectory();
            var cutDir = Path.Combine(root, "Cut");
            Directory.CreateDirectory(cutDir);

            // Исходное имя файла и расширение
            var srcName = Path.GetFileNameWithoutExtension(_currentFilePath);
            var ext = Path.GetExtension(_currentFilePath);

            var totalSegments = segments.Count;
            var successfulExports = 0;

            // Проходим по всем сегментам
            for (int i = 0; i < totalSegments; i++)
            {
                // Если запрошена отмена – останавливаемся
                if (_cts.Token.IsCancellationRequested)
                {
                    ExportStatus = "⏹ Экспорт отменён";
                    _messenger.Send(new ExportFinishedMessage(false));
                    break;
                }

                var seg = segments[i];
                if (seg == null)
                {
                    // На всякий случай пропускаем null-сегменты
                    continue;
                }

                // Имя выходного файла: номер_исходное_начальный_конечный.расширение
                var outFile = Path.Combine(cutDir,
                    $"{seg.Index:000}_{srcName}_{seg.StartFrame}_{seg.EndFrame}{ext}");

                ExportStatus = $"📤 Экспорт {i + 1} / {totalSegments}: {Path.GetFileName(outFile)}";

                var success = await ExportSingleSegment(seg, outFile, _cts.Token);
                if (success)
                    successfulExports++;

                // Обновляем прогресс в процентах
                ExportProgress = (int)((i + 1) * 100.0 / totalSegments);
            }

            // Завершаем экспорт
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

        /// <summary>
        /// Экспорт одного сегмента в файл с помощью FFmpeg.
        /// </summary>
        /// <param name="seg">Экспортируемый сегмент</param>
        /// <param name="outFile">Путь к выходному файлу</param>
        /// <param name="token">Токен отмены</param>
        /// <returns>true, если экспорт успешен</returns>
        private async Task<bool> ExportSingleSegment(SegmentInfo seg, string outFile, CancellationToken token)
        {
            try
            {
                // Преобразуем время в секунды (инвариантная культура для десятичной точки)
                var startTime = seg.Start.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var endTime = seg.End.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

                // Рассчитываем битрейт для видео (не менее 1500 kbps)
                var bitrateKbps = Math.Max(1500, (int)((_videoBitrate > 0 ? _videoBitrate : 4_000_000) / 1000));

                // Аргументы командной строки для FFmpeg:
                // -y            – перезаписывать выходной файл без запроса
                // -ss время     – начальная позиция
                // -to время     – конечная позиция
                // -i файл       – входной файл
                // -c:v libx264  – видеокодек H.264
                // -preset veryfast – быстрое кодирование (жертвуя сжатием)
                // -b:v битрейт  – битрейт видео
                // -c:a aac      – аудиокодек AAC
                // -ar 48000     – частота дискретизации аудио 48 кГц
                // -vsync cfr    – принудительная постоянная частота кадров
                // -async 1      – синхронизация аудио
                // -reset_timestamps 1 – сброс временных меток
                // -movflags +faststart – оптимизация для потокового воспроизведения
                var args = $"-y -ss {startTime} -to {endTime} -i \"{_currentFilePath}\" " +
                          $"-c:v libx264 -preset veryfast -b:v {bitrateKbps}k " +
                          $"-c:a aac -ar 48000 -vsync cfr -async 1 -reset_timestamps 1 " +
                          $"-movflags +faststart \"{outFile}\"";

                var success = await _ffmpeg.RunFfmpegAsync(args);

                // Если экспорт не удался, удаляем возможный битый файл
                if (!success && File.Exists(outFile))
                    File.Delete(outFile);

                return success;
            }
            catch (OperationCanceledException)
            {
                // Отмена операции – не ошибка
                return false;
            }
            catch (Exception ex)
            {
                // Логируем ошибку через шину сообщений
                _messenger.Send(new LogMessage($"Export error: {ex.Message}"));
                return false;
            }
        }

        /// <summary>
        /// Освобождение ресурсов. Отменяет экспорт и отписывается от сообщений.
        /// </summary>
        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _messenger.Unregister<SegmentsChangedMessage>(this);
            _messenger.Unregister<FileLoadedMessage>(this);
        }
    }
}