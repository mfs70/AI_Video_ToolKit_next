// Файл: ViewModels/MainViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Win32;
using AI_Video_ToolKit.Infrastructure.Services;
using AI_Video_ToolKit.UI.Dialogs;
using AI_Video_ToolKit.UI.Services;
using AI_Video_ToolKit.UI.Messages;
using AI_Video_ToolKit.UI.ViewModels;

namespace AI_Video_ToolKit.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly FFprobeService _ffprobe;
        private readonly FFmpegProcessService _ffmpeg;
        private readonly PlaybackService _playback;
        private readonly FrameGrabber _grabber;
        private readonly PlayerViewModel _playerVM;
        private readonly PlaylistViewModel _playlistVM;
        private readonly IMessenger _messenger;
        private readonly MarkersViewModel _markersVM;
        private readonly ExportViewModel _exportVM;

        public PlaylistViewModel PlaylistVM => _playlistVM;

        private string _statusText = "✅ Ready";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        // Прокси для маркеров
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
        private MontageItem? _selectedMontageItem;
        public MontageItem? SelectedMontageItem
        {
            get => _selectedMontageItem;
            set => SetProperty(ref _selectedMontageItem, value);
        }

        private readonly double[] _speeds = { 0.1, 0.25, 0.5, 1, 2, 4, 8, 16 };
        private int _speedIndex = 3;

        // Прокси для доступности экспорта (добавлено)
        public bool CanExport => _exportVM.CanExport;

        public MainViewModel(FFprobeService ffprobe, FFmpegProcessService ffmpeg, PlaybackService playback, FrameGrabber grabber,
            PlaylistViewModel playlistVM, PlayerViewModel playerVM, IMessenger messenger,
            MarkersViewModel markersVM, ExportViewModel exportVM)
        {
            _ffprobe = ffprobe;
            _ffmpeg = ffmpeg;
            _playback = playback;
            _grabber = grabber;
            _playlistVM = playlistVM;
            _playerVM = playerVM;
            _messenger = messenger;
            _markersVM = markersVM;
            _exportVM = exportVM;

            _playerVM.PropertyChanged += (s, e) => OnPropertyChanged(e.PropertyName);

            _playlistVM.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PlaylistViewModel.SelectedItem))
                    OnPropertyChanged(nameof(SelectedPlaylistItem));
            };

            _markersVM.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MarkersViewModel.SelectedSegment))
                    OnPropertyChanged(nameof(SelectedSegment));
            };
            _markersVM.MarkersChanged += () => MarkersChanged?.Invoke();

            // Подписка на изменения CanExport из ExportViewModel
            _exportVM.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ExportViewModel.CanExport) ||
                    e.PropertyName == nameof(ExportViewModel.IsBusy))
                {
                    OnPropertyChanged(nameof(CanExport));
                    OnPropertyChanged(nameof(IsExporting));
                }

                if (e.PropertyName == nameof(ExportViewModel.ExportProgress))
                    OnPropertyChanged(nameof(ExportProgress));

                if (e.PropertyName == nameof(ExportViewModel.ExportStatus))
                    OnPropertyChanged(nameof(ExportStatus));
            };

            _playback.OnPositionChanged += pos => RunOnUiThread(() => UpdatePosition(pos));
            _playback.OnPlaybackEnded += () => RunOnUiThread(() =>
            {
                IsPlaying = false;
                StatusText = "⏸ Paused";
            });

            _messenger.Register<FileLoadedMessage>(this, (r, m) =>
            {
                MarkersChanged?.Invoke();
                StatusText = "▶ Playing";
            });
            _messenger.Register<ImageLoadedMessage>(this, (r, m) => StatusText = "🖼 Image loaded");
            _messenger.Register<ExportedMediaMessage>(this, (_, message) =>
            {
                AddMontageItem(message.FilePath, message.Duration);
                StatusText = $"Added to montage: {Path.GetFileName(message.FilePath)}";
            });
        }

        public Task LoadFile(string path)
        {
            _messenger.Send(new LoadFileMessage(path));
            return Task.CompletedTask;
        }

        // Прокси-свойства PlayerViewModel
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

        // Прокси для экспорта
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

        public void Next() => NextCommand.Execute(null);
        public void Previous() => PreviousCommand.Execute(null);
        public bool AddToPlaylist(string path) => _playlistVM.AddToPlaylist(path);

        [RelayCommand]
        private async Task ExtractFrames()
        {
            _messenger.Send(new LogMessage("Action clicked: extract frames."));
            if (string.IsNullOrEmpty(CurrentFile))
            {
                StatusText = "No loaded video for frame extraction";
                _messenger.Send(new LogMessage("Extract frames skipped: no loaded video."));
                return;
            }

            var framesDir = Path.Combine(Directory.GetCurrentDirectory(), "Frames");
            Directory.CreateDirectory(framesDir);
            framesDir = ChooseFolder("Select folder for extracted frames", framesDir);
            if (string.IsNullOrWhiteSpace(framesDir))
            {
                _messenger.Send(new LogMessage("Extract frames cancelled by user."));
                return;
            }

            StatusText = "Extracting frames...";
            var outputPattern = Path.Combine(framesDir, "frame_%06d.png");
            var args = $"-y -hide_banner -i \"{CurrentFile}\" -vsync 0 -progress pipe:1 -nostats \"{outputPattern}\"";
            var success = await RunProgressFfmpegAsync(args, GetCurrentMediaDuration(), "Extracting frames...");
            StatusText = success ? $"Frames saved: {framesDir}" : "Frame extraction failed";
            _messenger.Send(new LogMessage(success
                ? $"Extract frames complete: {framesDir}"
                : "Extract frames failed."));
        }

        [RelayCommand]
        private async Task BuildVideoFromFrames()
        {
            _messenger.Send(new LogMessage("Action clicked: build video from Frames."));
            var root = Directory.GetCurrentDirectory();
            var framesDir = Path.Combine(root, "Frames");
            Directory.CreateDirectory(framesDir);
            framesDir = ChooseFolder("Select folder with frames", framesDir);
            if (string.IsNullOrWhiteSpace(framesDir))
            {
                _messenger.Send(new LogMessage("Build from frames cancelled by user."));
                return;
            }

            var images = GetFrameImages(framesDir);
            if (images.Count == 0)
            {
                StatusText = "Frames folder is empty";
                _messenger.Send(new LogMessage($"Build video skipped: no images in {framesDir}."));
                return;
            }

            var options = new FrameBuildOptionsDialog(framesDir)
            {
                Owner = Application.Current?.MainWindow
            };
            if (options.ShowDialog() != true)
            {
                _messenger.Send(new LogMessage("Build from frames cancelled in options dialog."));
                return;
            }

            var outputDir = Path.Combine(root, "Output");
            Directory.CreateDirectory(outputDir);
            var fps = FileFps > 0 ? FileFps : 25;
            var extension = NormalizeExtension(options.SelectedFormat);
            var outFile = Path.Combine(outputDir, $"frames_{DateTime.Now:yyyyMMdd_HHmmss}{extension}");
            StatusText = "Building video from frames...";
            var qualityArgs = GetQualityArguments(options.SelectedQuality);
            var expectedDuration = images.Count == 1
                ? TimeSpan.FromSeconds(4)
                : TimeSpan.FromSeconds(images.Count / Math.Max(1, fps));

            string? concatList = null;
            string args;
            if (images.Count == 1)
            {
                // A single still image becomes a four-second video clip.
                args = $"-y -hide_banner -loop 1 -t 4 -i \"{images[0]}\" " +
                       $"-vf fps={fps.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                       $"-c:v libx264 {qualityArgs} -pix_fmt yuv420p -progress pipe:1 -nostats \"{outFile}\"";
            }
            else
            {
                // The concat list supports arbitrary file names, not only frame_000001.png.
                concatList = CreateImageConcatList(images, fps, outputDir);
                args = $"-y -hide_banner -f concat -safe 0 -i \"{concatList}\" -vsync vfr " +
                       $"-c:v libx264 {qualityArgs} -pix_fmt yuv420p -progress pipe:1 -nostats \"{outFile}\"";
            }

            var success = await RunProgressFfmpegAsync(args, expectedDuration, "Building video from frames...");
            if (!string.IsNullOrEmpty(concatList) && File.Exists(concatList))
                File.Delete(concatList);

            if (success)
                AddMontageItem(outFile, expectedDuration);

            StatusText = success ? $"Video built: {Path.GetFileName(outFile)}" : "Build from frames failed";
            _messenger.Send(new LogMessage(success
                ? $"Build from frames complete: {outFile}"
                : "Build from frames failed."));
        }

        [RelayCommand]
        private async Task MergeMontage()
        {
            _messenger.Send(new LogMessage("Action clicked: merge all montage clips."));
            await MergeMontageItems(MontageItems.ToList(), "montage_all");
        }

        public async Task MergeSelectedMontageItems(IEnumerable<MontageItem> selectedItems)
        {
            _messenger.Send(new LogMessage("Action clicked: merge selected montage clips."));
            await MergeMontageItems(selectedItems.ToList(), "montage_selected");
        }

        private async Task MergeMontageItems(IReadOnlyList<MontageItem> items, string namePrefix)
        {
            if (items.Count == 0)
            {
                StatusText = "No montage clips selected";
                _messenger.Send(new LogMessage("Merge skipped: no montage clips."));
                return;
            }

            var root = Directory.GetCurrentDirectory();
            var outputDir = Path.Combine(root, "Output");
            Directory.CreateDirectory(outputDir);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var listFile = Path.Combine(outputDir, $"{namePrefix}_{stamp}.txt");
            var outFile = Path.Combine(outputDir, $"{namePrefix}_{stamp}.mp4");
            File.WriteAllLines(listFile, items.Select(x => $"file '{EscapeConcatPath(x.FilePath)}'"));

            StatusText = $"Merging {items.Count} clip(s)...";
            var args = $"-y -hide_banner -f concat -safe 0 -i \"{listFile}\" -c copy \"{outFile}\"";
            var success = await _ffmpeg.RunFfmpegAsync(args);
            if (success)
                AddMontageItem(outFile, TimeSpan.FromTicks(items.Sum(x => x.Duration.Ticks)));

            StatusText = success ? $"Merge complete: {Path.GetFileName(outFile)}" : "Merge failed";
            _messenger.Send(new LogMessage(success
                ? $"Merge complete: {outFile}"
                : "Merge failed."));
        }

        private void AddMontageItem(string filePath, TimeSpan duration)
        {
            if (MontageItems.Any(x => string.Equals(x.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
                return;

            MontageItems.Add(new MontageItem
            {
                FilePath = filePath,
                TypeIcon = "🎬",
                Duration = duration,
                Order = MontageItems.Count + 1
            });
            _ = LoadMontageThumbnailAsync(MontageItems[^1]);
        }

        public void RemoveMontageItems(IEnumerable<MontageItem> items)
        {
            foreach (var item in items.ToList())
                MontageItems.Remove(item);

            RenumberMontageItems();
            _messenger.Send(new LogMessage("Montage selection removed from table only; files in Cut were preserved."));
        }

        public void MoveMontageItem(MontageItem dragged, MontageItem target)
        {
            var oldIndex = MontageItems.IndexOf(dragged);
            var newIndex = MontageItems.IndexOf(target);
            if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex)
                return;

            MontageItems.Move(oldIndex, newIndex);
            RenumberMontageItems();
            _messenger.Send(new LogMessage($"Montage clip moved: {dragged.FileName} -> position {newIndex + 1:000}."));
        }

        public async Task AddFileToMontageFromDrop(string sourcePath)
        {
            if (!File.Exists(sourcePath))
                return;

            var root = Directory.GetCurrentDirectory();
            var cutDir = Path.Combine(root, "Cut");
            Directory.CreateDirectory(cutDir);
            var info = await _ffprobe.GetInfoAsync(sourcePath);
            var fps = info.Fps > 0 ? info.Fps : 25;
            var duration = TimeSpan.FromSeconds(Math.Max(0, info.Duration));
            var endFrame = Math.Max(0, (long)Math.Round(duration.TotalSeconds * fps) - 1);
            var index = MontageItems.Count + 1;
            var name = Path.GetFileNameWithoutExtension(sourcePath);
            var ext = Path.GetExtension(sourcePath);
            var destination = Path.Combine(cutDir, $"{index:000}_{name}_0_{endFrame}{ext}");

            StatusText = $"Adding to montage: {Path.GetFileName(sourcePath)}";
            _messenger.Send(new LogMessage($"Montage drop: copying/transcoding {sourcePath} to Cut."));
            var args = $"-y -hide_banner -i \"{sourcePath}\" -c:v libx264 -preset veryfast -crf 18 -c:a aac -progress pipe:1 -nostats \"{destination}\"";
            var success = await RunProgressFfmpegAsync(args, duration > TimeSpan.Zero ? duration : TimeSpan.FromSeconds(1), "Adding clip to montage...");
            if (success)
            {
                AddMontageItem(destination, duration);
                StatusText = $"Added to montage: {Path.GetFileName(destination)}";
            }
            else
            {
                StatusText = "Failed to add clip to montage";
            }
        }

        private async Task LoadMontageThumbnailAsync(MontageItem item)
        {
            var bitmap = await _grabber.GetFrame(item.FilePath, TimeSpan.Zero, 160, 90);
            if (bitmap != null)
                item.Thumbnail = bitmap;
        }

        private void RenumberMontageItems()
        {
            for (var i = 0; i < MontageItems.Count; i++)
                MontageItems[i].Order = i + 1;
        }

        private static string EscapeConcatPath(string path) => path.Replace("\\", "/").Replace("'", "'\\''");

        private async Task<bool> RunProgressFfmpegAsync(string args, TimeSpan expectedDuration, string status)
        {
            IsExporting = true;
            ExportProgress = 0;
            ExportStatus = status;
            var progress = new Progress<double>(value =>
            {
                ExportProgress = (int)Math.Round(Math.Clamp(value, 0, 1) * 100);
                ExportStatus = $"{status} {ExportProgress}%";
            });

            try
            {
                var success = await _ffmpeg.RunFfmpegAsync(args, expectedDuration, progress, CancellationToken.None);
                ExportProgress = success ? 100 : 0;
                ExportStatus = success ? "Operation complete" : "Operation failed";
                return success;
            }
            finally
            {
                await Task.Delay(350);
                IsExporting = false;
                ExportProgress = 0;
            }
        }

        private TimeSpan GetCurrentMediaDuration()
        {
            if (_playerVM.TotalDuration > TimeSpan.Zero)
                return _playerVM.TotalDuration;

            if (FileDurationSec > 0)
                return TimeSpan.FromSeconds(FileDurationSec);

            return TimeSpan.FromSeconds(1);
        }

        private static string? ChooseFolder(string title, string initialDirectory)
        {
            var dialog = new OpenFolderDialog
            {
                Title = title,
                InitialDirectory = Directory.Exists(initialDirectory)
                    ? initialDirectory
                    : Directory.GetCurrentDirectory()
            };

            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }

        private static IReadOnlyList<string> GetFrameImages(string framesDir)
        {
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".webp"
            };

            return Directory.EnumerateFiles(framesDir)
                .Where(path => extensions.Contains(Path.GetExtension(path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string CreateImageConcatList(IReadOnlyList<string> images, double fps, string outputDir)
        {
            var listFile = Path.Combine(outputDir, $"frames_concat_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt");
            var frameDuration = 1.0 / Math.Max(1, fps);
            var lines = new List<string>();
            foreach (var image in images)
            {
                lines.Add($"file '{EscapeConcatPath(image)}'");
                lines.Add($"duration {frameDuration.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }

            lines.Add($"file '{EscapeConcatPath(images[^1])}'");
            File.WriteAllLines(listFile, lines);
            return listFile;
        }

        private static string NormalizeExtension(string extension)
            => extension.StartsWith(".") ? extension : $".{extension}";

        private static string GetQualityArguments(string preset)
            => preset switch
            {
                "Low" => "-preset veryfast -crf 28",
                "High" => "-preset slow -crf 18",
                _ => "-preset medium -crf 23"
            };

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

    public partial class MontageItem : ObservableObject
    {
        public string FilePath { get; set; } = "";
        public string FileName => Path.GetFileName(FilePath);
        public string TypeIcon { get; set; } = "🎬";
        private int _order;
        public int Order
        {
            get => _order;
            set
            {
                if (SetProperty(ref _order, value))
                    OnPropertyChanged(nameof(IndexLabel));
            }
        }

        public TimeSpan Duration { get; set; }
        public string DurationStr => Duration.ToString(@"hh\:mm\:ss\.fff");
        public string IndexLabel => Order.ToString("000");

        private BitmapSource? _thumbnail;
        public BitmapSource? Thumbnail
        {
            get => _thumbnail;
            set => SetProperty(ref _thumbnail, value);
        }
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
