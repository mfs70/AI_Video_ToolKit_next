// Файл: ViewModels/PlayerViewModel.cs
/*  1. Генерация исходного кода
    Атрибуты [ObservableProperty] и [RelayCommand] заставляют генератор CommunityToolkit.Mvvm автоматически создавать публичные свойства и команды
    (например, IsPlaying, PlayPauseCommand). Это избавляет от шаблонного кода.
    2. Внедрение зависимостей
    Все внешние сервисы (PlaybackService, FFprobeService, IMessenger) приходят через конструктор.
    Это делает ViewModel тестируемой и слабосвязанной.
    3. Маршалинг в UI-поток
    События плеера приходят из фонового потока. _uiDispatcher.BeginInvoke(...) перенаправляет обновление свойств в поток диспетчера WPF,
    чтобы избежать исключений о доступе к UI из другого потока.
    4. Обмен сообщениями
    IMessenger позволяет разным ViewModel общаться, не зная друг о друге. Здесь PlayerViewModel слушает LoadFileMessage,
    а сам отправляет FileLoadedMessage и LogMessage.
    5. Обработка команд
    Методы, помеченные [RelayCommand], становятся доступными для привязки в XAML (например, Command="{Binding PlayPauseCommand}").
    Команда Seek принимает параметр TimeSpan — его можно передать из Slider.Value через CommandParameter.
    6. Вычисляемые свойства
    Свойства FileFps и TotalDuration не могут быть автоматически сгенерированы,
    поэтому после изменения базовых данных вручную вызывается OnPropertyChanged(nameof(FileFps)).
*/

using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;   // Для [ObservableProperty] и базового класса
using CommunityToolkit.Mvvm.Input;          // Для [RelayCommand]
using CommunityToolkit.Mvvm.Messaging;      // Для IMessenger и сообщений
using AI_Video_ToolKit.Domain;              // Модели предметной области (MediaInfo и др.)
using AI_Video_ToolKit.Infrastructure.Services; // Сервисы низкого уровня (FFprobe, проигрывание)
using AI_Video_ToolKit.UI.Services;         // UI-сервисы
using AI_Video_ToolKit.UI.Messages;         // Сообщения для общения между ViewModel

namespace AI_Video_ToolKit.UI.ViewModels
{
    /// <summary>
    /// ViewModel главного плеера. Отвечает за загрузку файла,
    /// управление воспроизведением и предоставление метаданных для UI.
    /// </summary>
    public partial class PlayerViewModel : ObservableObject, IDisposable
    {
        // Сервисы, внедряемые через DI
        private readonly PlaybackService _playback;   // Управление воспроизведением (старт, пауза, позиция)
        private readonly FFprobeService _ffprobe;     // Извлечение метаданных медиафайла
        private readonly IMessenger _messenger;       // Шина сообщений для связи с другими VM
        private readonly Dispatcher _uiDispatcher;    // Диспетчер UI-потока для маршалинга обновлений свойств

        // Кэшированная информация о текущем файле
        private MediaInfo? _currentInfo;
        private bool _disposed;

        // ================================================================
        // Наблюдаемые свойства (генерируются генератором исходного кода)
        // Атрибут [ObservableProperty] автоматически создаёт публичное свойство
        // и частичный метод On<Имя>Changed().
        // ================================================================
        [ObservableProperty] private bool _isPlaying;
        [ObservableProperty] private TimeSpan _currentPosition;
        [ObservableProperty] private double _speed = 1.0;
        [ObservableProperty] private string _currentFileName = "";
        [ObservableProperty] private string _resolution = "";
        [ObservableProperty] private string _fpsStr = "";
        [ObservableProperty] private string _codec = "";
        [ObservableProperty] private string _bitrate = "";
        [ObservableProperty] private string _duration = "";
        [ObservableProperty] private string _audioInfo = "";
        [ObservableProperty] private long _currentFrame;
        [ObservableProperty] private long _totalFrames;

        // Вычисляемое свойство, не генерируется автоматически,
        // поэтому вручную уведомляем об изменении через OnPropertyChanged
        public double FileFps => _currentInfo?.Fps ?? 25.0;

        // Публичные поля/свойства для других частей UI
        public string CurrentFilePath { get; private set; } = "";
        public double DurationSeconds { get; private set; }
        public double Fps { get; private set; }
        public bool HasAudio { get; private set; }
        public long VideoBitrate { get; private set; }
        public TimeSpan TotalDuration => TimeSpan.FromSeconds(DurationSeconds);

        /// <summary>
        /// Конструктор получает зависимости через DI.
        /// </summary>
        public PlayerViewModel(PlaybackService playback, FFprobeService ffprobe, IMessenger messenger)
        {
            _playback = playback;
            _ffprobe = ffprobe;
            _messenger = messenger;
            _uiDispatcher = Dispatcher.CurrentDispatcher; // Запоминаем диспетчер UI-потока

            // Подписываемся на события плеера, маршалируя их в UI-поток
            _playback.OnPositionChanged += pos => _uiDispatcher.BeginInvoke(() => CurrentPosition = pos);
            _playback.OnFrameChanged += frame => { }; // Можно реализовать позже
            _playback.OnPlaybackEnded += () => _uiDispatcher.BeginInvoke(() => IsPlaying = false);

            // Регистрируемся на сообщение о загрузке файла (от другого ViewModel или сервиса)
            _messenger.Register<LoadFileMessage>(this, async (r, m) => await LoadFile(m.FilePath));
        }

        /// <summary>
        /// Основной метод загрузки видеофайла.
        /// </summary>
        private async Task LoadFile(string path)
        {
            try
            {
                _playback.Stop();     // Остановить предыдущее воспроизведение
                IsPlaying = false;

                // 1. Получить метаданные через FFprobe
                var info = await _ffprobe.GetInfoAsync(path);
                _currentInfo = info;

                // Отладочный вывод (можно удалить после проверки)
                System.Diagnostics.Debug.WriteLine($"Resolution={Resolution}, Fps={FpsStr}");

                // 2. Заполнить внутренние поля и свойства для UI
                DurationSeconds = info.Duration;
                Fps = info.Fps;
                HasAudio = info.HasAudio;
                VideoBitrate = info.VideoBitrate;
                CurrentFilePath = path;

                // Основные метаданные, отображаемые в статус-баре
                CurrentFileName = Path.GetFileName(path);
                Resolution = $"{info.Width}x{info.Height}";
                FpsStr = $"{info.Fps:0.##}";
                Codec = info.VideoCodec;
                Bitrate = $"{info.VideoBitrate / 1000:0} kbps";
                Duration = info.Duration > 0 ? TimeSpan.FromSeconds(info.Duration).ToString(@"hh\:mm\:ss") : "??:??:??";
                AudioInfo = info.HasAudio
                    ? $"{info.AudioCodec} {info.AudioSampleRate / 1000.0:F1}kHz {info.AudioChannels}ch {info.AudioBitrate / 1000:0}kbps"
                    : "none";
                TotalFrames = (long)(info.Duration * info.Fps);
                CurrentFrame = 0;
                CurrentPosition = TimeSpan.Zero;

                // 3. Начать воспроизведение с текущей скоростью
                _playback.Start(path, info.Fps, TimeSpan.Zero, Speed, info.HasAudio);
                IsPlaying = true;

                // 4. Оповестить другие ViewModel через сообщение
                _messenger.Send(new FileLoadedMessage(path, info.Duration, info.Fps, info.HasAudio, info.VideoBitrate));

                // Вручную уведомить об изменении вычисляемых свойств
                OnPropertyChanged(nameof(FileFps));
                OnPropertyChanged(nameof(TotalDuration));
                // Отладочный вывод (можно удалить после проверки)
                System.Diagnostics.Debug.WriteLine($"PlayerViewModel: Resolution={Resolution}, Fps={FpsStr}");
            }
            catch (Exception ex)
            {
                // В случае ошибки отправляем сообщение в лог
                _messenger.Send(new LogMessage($"PlayerViewModel error: {ex.Message}"));
            }
        }

        // Команда Play/Pause, будет сгенерирован метод PlayPauseCommand
        [RelayCommand]
        private void PlayPause()
        {
            if (IsPlaying)
            {
                _playback.Pause();
                IsPlaying = false;
            }
            else
            {
                _playback.Resume();
                IsPlaying = true;
            }
        }

        // Команда Stop
        [RelayCommand]
        private void Stop()
        {
            _playback.Stop();
            IsPlaying = false;
            CurrentPosition = TimeSpan.Zero;
            CurrentFrame = 0;
        }

        // Команда перемотки. Принимает параметр TimeSpan из UI (например, от слайдера)
        [RelayCommand]
        private void Seek(TimeSpan position)
        {
            // Ограничиваем позицию допустимым диапазоном
            if (position.TotalSeconds < 0) position = TimeSpan.Zero;
            if (_currentInfo != null && position.TotalSeconds > _currentInfo.Duration)
                position = TimeSpan.FromSeconds(_currentInfo.Duration);

            _playback.SetPosition(position);
            CurrentPosition = position;
        }

        // Частичный метод, вызываемый при изменении свойства Speed.
        // Генерируется CommunityToolkit.Mvvm для [ObservableProperty] double _speed.
        partial void OnSpeedChanged(double value)
        {
            _playback.SetSpeed(value);
        }

        // Освобождение ресурсов
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _playback.Stop();
            _messenger.Unregister<LoadFileMessage>(this); // Отписываемся от сообщения
        }
    }
}
