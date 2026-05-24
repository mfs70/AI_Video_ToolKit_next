// Файл: ViewModels/MainViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Win32;
using AI_Video_ToolKit.Infrastructure.Services;
using AI_Video_ToolKit.UI.Services;
using AI_Video_ToolKit.UI.Messages;
using AI_Video_ToolKit.UI.ViewModels;

namespace AI_Video_ToolKit.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly FFprobeService _ffprobe;
        //        private readonly FFmpegProcessService _ffmpeg;
        private readonly PlaybackService _playback;
        private readonly FrameGrabber _grabber;
        private readonly PlayerViewModel _playerVM;
        private readonly PlaylistViewModel _playlistVM;
        private readonly IMessenger _messenger;
        private readonly MarkersViewModel _markersVM;
        private readonly ExportViewModel _exportVM;

        public PlaylistViewModel PlaylistVM => _playlistVM;

        // Состояние
        private string _statusText = "✅ Ready";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        // Маркеры и сегменты

        //        public ObservableCollection<SegmentInfo> Segments { get; } = new();
        public ObservableCollection<SegmentInfo> Segments => _markersVM.Segments;
        public SegmentInfo? SelectedSegment
        {
            get => _markersVM.SelectedSegment;
            set => _markersVM.SelectedSegment = value;
        }
        public ICommand MarkInputCommand => _markersVM.MarkInputCommand;
        public ICommand MarkOutputCommand => _markersVM.MarkOutputCommand;
        public ICommand MarkCutCommand => _markersVM.MarkCutCommand;
        public ICommand UndoMarkerCommand => _markersVM.UndoMarkerCommand;
        public ICommand ClearCutsCommand => _markersVM.ClearCutsCommand;
        public (double duration, TimeSpan? input, TimeSpan? output, IReadOnlyList<TimeSpan> cuts) GetTimelineData()
            => _markersVM.GetTimelineData();
        public void MoveTimelineMarker(string markerType, TimeSpan? original, TimeSpan moved)
            => _markersVM.MoveTimelineMarker(markerType, original, moved);

        public event Action? MarkersChanged;
        public event Action<BitmapImage>? ImageLoaded;

        public ObservableCollection<MontageItem> MontageItems { get; } = new();

        // Скорость
        private readonly double[] _speeds = { 0.1, 0.25, 0.5, 1, 2, 4, 8, 16 };
        private int _speedIndex = 3;

        // Конструктор
        public MainViewModel(FFprobeService ffprobe, PlaybackService playback, FrameGrabber grabber,
         PlaylistViewModel playlistVM, PlayerViewModel playerVM, IMessenger messenger, MarkersViewModel markersVM,
          ExportViewModel exportVM)
        {
            _ffprobe = ffprobe;
            //            _ffmpeg = ffmpeg;
            _playback = playback;
            _grabber = grabber;
            _playlistVM = playlistVM;
            _playerVM = playerVM;
            _messenger = messenger;
            _markersVM = markersVM;
            _exportVM = exportVM;
            // Подписка на изменения свойств PlayerViewModel для проброса в UI
            _playerVM.PropertyChanged += (s, e) =>
            {
                // Проксируем все изменения свойств из PlayerViewModel в MainViewModel
                OnPropertyChanged(e.PropertyName);
            };

            // Подписка на изменения свойств PlaylistViewModel (для SelectedItem)
            _playlistVM.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PlaylistViewModel.SelectedItem))
                    OnPropertyChanged(nameof(SelectedPlaylistItem));
                // Можно добавить другие проксируемые свойства, если потребуется
            };

            //подписка на  событие MarkersChanged для проброса в UI (если нужно обновлять что-то в UI, например, TimelineControl)
            //  и перенаправлять событие
            _markersVM.MarkersChanged += () => MarkersChanged?.Invoke();

            _playback.OnPositionChanged += pos =>
            {
                RunOnUiThread(() => UpdatePosition(pos));
            };
            _playback.OnPlaybackEnded += () =>
            {
                RunOnUiThread(() =>
                {
                    IsPlaying = false;
                    StatusText = "⏸ Paused";
                });
            };

            // Подписываемся на сообщение о загрузке файла, чтобы синхронизировать маркеры и сброс состояния
            _messenger.Register<FileLoadedMessage>(this, (r, m) =>
            {
                // Сброс маркеров, когда загружен новый файл
                MarkersChanged?.Invoke();
                StatusText = "▶ Playing";
            });

            // Регистрация на сообщение для изображений (опционально)
            _messenger.Register<ImageLoadedMessage>(this, (r, m) =>
            {
                // Здесь можно загрузить изображение в Preview, но пока оставим как есть
                StatusText = "🖼 Image loaded";
            });
        }

        // Временный прокси-метод для совместимости со старым кодом в MainWindow.xaml.cs
        public Task LoadFile(string path)
        {
            _messenger.Send(new LoadFileMessage(path));
            return Task.CompletedTask;
        }


        // Прокси-свойства (напрямую из PlayerViewModel)
        public bool IsPlaying { get => _playerVM.IsPlaying; set => _playerVM.IsPlaying = value; }
        public TimeSpan CurrentPosition { get => _playerVM.CurrentPosition; set => _playerVM.CurrentPosition = value; }
        public double Speed => _playerVM.Speed;
        public int SelectedSpeedIndex
        {
            get => _speedIndex;
            set
            {
                if (_speedIndex == value) return;
                _speedIndex = value;
                OnPropertyChanged();
                _playerVM.Speed = _speeds[_speedIndex];
            }
        }
        public string CurrentFileName { get => _playerVM.CurrentFileName; set => _playerVM.CurrentFileName = value; }
        public string Resolution { get => _playerVM.Resolution; set => _playerVM.Resolution = value; }
        public string FpsStr { get => _playerVM.FpsStr; set => _playerVM.FpsStr = value; }
        public string Codec { get => _playerVM.Codec; set => _playerVM.Codec = value; }
        public string Bitrate { get => _playerVM.Bitrate; set => _playerVM.Bitrate = value; }
        public string Duration { get => _playerVM.Duration; set => _playerVM.Duration = value; }
        public string AudioInfo { get => _playerVM.AudioInfo; set => _playerVM.AudioInfo = value; }
        public long CurrentFrame { get => _playerVM.CurrentFrame; set => _playerVM.CurrentFrame = value; }
        public long TotalFrames { get => _playerVM.TotalFrames; set => _playerVM.TotalFrames = value; }
        public string TotalTimeStr => _playerVM.TotalDuration.ToString(@"hh\:mm\:ss");
        public string CurrentFile => _playerVM.CurrentFilePath;
        public double FileDurationSec => _playerVM.DurationSeconds;
        public double FileFps => _playerVM.Fps;
        public bool HasAudio => _playerVM.HasAudio;
        public long VideoBitrate => _playerVM.VideoBitrate;

        // Прокси-свойства для прогресс-бара
        public int ExportProgress
        {
            get => _exportVM.ExportProgress;
            set => _exportVM.ExportProgress = value;
        }
        public bool IsExporting
        {
            get => _exportVM.IsBusy;
            set => _exportVM.IsBusy = value;
        }
        public string ExportStatus
        {
            get => _exportVM.ExportStatus;
            set => _exportVM.ExportStatus = value;
        }

        // Прокси для плейлиста
        public ObservableCollection<PlaylistItem> PlaylistItems => _playlistVM.Items;
        public PlaylistItem? SelectedPlaylistItem
        {
            get => _playlistVM.SelectedItem;
            set => _playlistVM.SelectedItem = value;
        }
        public ICommand ClearPlaylistCommand => _playlistVM.ClearCommand;
        public ICommand RemoveSelectedFromPlaylistCommand => _playlistVM.RemoveSelectedCommand;
        public ICommand NextCommand => _playlistVM.MoveNextCommand;
        public ICommand PreviousCommand => _playlistVM.MovePreviousCommand;
        public ICommand AddFilesCommand => _playlistVM.AddFilesCommand;
        public ICommand ExportSelectedCommand => _exportVM.ExportSelectedCommand;
        public ICommand ExportAllCommand => _exportVM.ExportAllCommand;
        public ICommand CancelExportCommand => _exportVM.CancelExportCommand;

        // Прокси-команды плеера
        public ICommand PlayPauseCommand => _playerVM.PlayPauseCommand;
        public ICommand StopCommand => _playerVM.StopCommand;
        public ICommand SeekCommand => _playerVM.SeekCommand;

        // Для совместимости со старыми обработчиками в MainWindow.xaml.cs
        public void Next() => NextCommand.Execute(null);
        public void Previous() => PreviousCommand.Execute(null);
        public bool AddToPlaylist(string path) => _playlistVM.AddToPlaylist(path);

        // Маркеры

        // Предпросмотр и экспорт
        [RelayCommand]
        private async Task PreviewSegment()
        {
            if (SelectedSegment == null) return;
            _playback.Stop();
            _playback.Start(CurrentFile, FileFps, SelectedSegment.Start, Speed, HasAudio, SelectedSegment.End);
            IsPlaying = true;
            StatusText = "▶ Preview Segment";
            await Task.CompletedTask;
        }

        //        public void UpdatePosition(TimeSpan pos)
        // {
        //  CurrentPosition = pos;
        //  CurrentFrame = TimeToFrame(pos);
        //  OnPropertyChanged(nameof(CurrentTimeStr));
        // }

        public void UpdatePosition(TimeSpan pos)
        {
            CurrentPosition = pos;
            CurrentFrame = (long)(pos.TotalSeconds * _playerVM.Fps);
            OnPropertyChanged(nameof(CurrentTimeStr));
        }

        public string CurrentTimeStr => CurrentPosition.ToString(@"hh\:mm\:ss");

        private static void RunOnUiThread(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }
            dispatcher.BeginInvoke(action);
        }

        // Загрузка изображений (специфичная логика, пока оставим здесь, но можно перенести в PlayerViewModel)
        private void LoadImageFile(string path)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            CurrentFileName = Path.GetFileName(path);
            Resolution = $"{bitmap.PixelWidth}x{bitmap.PixelHeight}";
            FpsStr = "-";
            Codec = "image";
            Bitrate = "-";
            Duration = "00:00:00";
            AudioInfo = "none";
            CurrentPosition = TimeSpan.Zero;
            CurrentFrame = 0;
            TotalFrames = 1;
            OnPropertyChanged(nameof(FileFps));
            Segments.Clear();
            IsPlaying = false;
            StatusText = "🖼 Image loaded";
            OnPropertyChanged(nameof(CurrentTimeStr));
            OnPropertyChanged(nameof(TotalTimeStr));
            MarkersChanged?.Invoke();
            ImageLoaded?.Invoke(bitmap);
        }

        private static bool IsImage(string path) => Path.GetExtension(path).ToLower() is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif";
    }

    // Вспомогательные классы (без изменений)
    public class PlaylistItem
    {
        public string FilePath { get; set; } = "";
        public string FileName => Path.GetFileName(FilePath);
        public string Extension => Path.GetExtension(FilePath).ToLower();
        public bool IsVideo => Extension is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm";
        public bool IsImage => Extension is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif";
        public string TypeIcon => IsVideo ? "🎬" : (IsImage ? "🖼️" : "📄");
    }

    public class MontageItem
    {
        public string FilePath { get; set; } = "";
        public string FileName => Path.GetFileName(FilePath);
        public string TypeIcon { get; set; } = "🎬";
        public TimeSpan Duration { get; set; }
        public string DurationStr => Duration.ToString(@"hh\:mm\:ss\.fff");
    }

    public class SegmentInfo
    {
        public int Index { get; set; }
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
        public long StartFrame { get; set; }
        public long EndFrame { get; set; }
        public TimeSpan Duration => End - Start;
        public override string ToString() => $"{Index:000}_{Start:hh\\:mm\\:ss\\.fff}_{End:hh\\:mm\\:ss\\.fff}";
    }

    internal enum MarkerActionType { InputSet, OutputSet, CutAdd, CutClear }
}