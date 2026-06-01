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

namespace AI_Video_ToolKit.UI.ViewModels
{
    public partial class ExportViewModel : ObservableObject, IDisposable
    {
        private readonly FFmpegProcessService _ffmpeg;
        private readonly IMessenger _messenger;
        private CancellationTokenSource? _cts;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private int _exportProgress;

        [ObservableProperty]
        private string _exportStatus = "Ready";

        private IReadOnlyList<SegmentInfo> _segments = Array.Empty<SegmentInfo>();
        private string _currentFilePath = string.Empty;
        private long _videoBitrate;
        private DateTime _lastProgressUiUpdate = DateTime.MinValue;
        private int _lastReportedProgress = -1;

        public bool CanExport => !IsBusy && _segments.Any();

        partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanExport));

        public ExportViewModel(FFmpegProcessService ffmpeg, IMessenger messenger)
        {
            _ffmpeg = ffmpeg;
            _messenger = messenger;

            _messenger.Register<SegmentsChangedMessage>(this, (_, message) =>
            {
                _segments = message.Segments;
                OnPropertyChanged(nameof(CanExport));
            });

            _messenger.Register<FileLoadedMessage>(this, (_, message) =>
            {
                _currentFilePath = message.FilePath;
                _videoBitrate = message.VideoBitrate;
            });
        }

        [RelayCommand]
        private async Task ExportSelected(SegmentInfo? segment)
        {
            if (segment == null)
            {
                ExportStatus = "Select a segment before export";
                _messenger.Send(new LogMessage("Export selected clicked: no segment selected."));
                return;
            }

            _messenger.Send(new LogMessage($"Export selected clicked: segment {segment.Index}."));
            await ExportSegments(new List<SegmentInfo> { segment });
        }

        [RelayCommand]
        private async Task ExportAll()
        {
            if (_segments.Count == 0)
            {
                ExportStatus = "No segments to export";
                _messenger.Send(new LogMessage("Export all clicked: no segments."));
                return;
            }

            _messenger.Send(new LogMessage($"Export all clicked: {_segments.Count} segment(s)."));
            await ExportSegments(_segments);
        }

        [RelayCommand]
        private void CancelExport()
        {
            _cts?.Cancel();
            ExportStatus = "Cancelling export...";
            _messenger.Send(new LogMessage("Cancel export clicked."));
            _messenger.Send(new ExportCancelledMessage());
        }

        private async Task ExportSegments(IReadOnlyList<SegmentInfo> segments)
        {
            if (IsBusy) return;
            if (string.IsNullOrEmpty(_currentFilePath))
            {
                ExportStatus = "No loaded file for export";
                _messenger.Send(new LogMessage("Export skipped: no loaded file."));
                return;
            }

            IsBusy = true;
            ExportProgress = 0;
            _lastProgressUiUpdate = DateTime.MinValue;
            _lastReportedProgress = -1;
            _cts = new CancellationTokenSource();
            _messenger.Send(new ExportStartedMessage());
            _messenger.Send(new LogMessage($"Export started: {segments.Count} segment(s)."));

            var root = Directory.GetCurrentDirectory();
            var cutDir = Path.Combine(root, "Cut");
            Directory.CreateDirectory(cutDir);

            var srcName = Path.GetFileNameWithoutExtension(_currentFilePath);
            var ext = Path.GetExtension(_currentFilePath);
            var totalSegments = segments.Count;
            var successfulExports = 0;

            for (var i = 0; i < totalSegments; i++)
            {
                if (_cts.Token.IsCancellationRequested)
                    break;

                var seg = segments[i];
                if (seg == null) continue;

                var outFile = Path.Combine(cutDir,
                    $"{seg.Index:000}_{srcName}_{seg.StartFrame}_{seg.EndFrame}{ext}");

                ExportStatus = $"Export {i + 1}/{totalSegments}: {Path.GetFileName(outFile)}";

                var segmentIndex = i;
                var segmentProgress = new Progress<double>(value =>
                {
                    // Throttle progress notifications so long exports do not flood the UI thread.
                    var total = (segmentIndex + Math.Clamp(value, 0, 1)) * 100.0 / totalSegments;
                    ReportProgress((int)Math.Clamp(total, 0, 100), force: value >= 1);
                });

                var success = await ExportSingleSegment(seg, outFile, _cts.Token, segmentProgress);
                if (success)
                {
                    successfulExports++;
                    _messenger.Send(new ExportedMediaMessage(outFile, seg.Duration));
                    _messenger.Send(new LogMessage($"Exported segment: {Path.GetFileName(outFile)}"));
                }

                ReportProgress((int)((i + 1) * 100.0 / totalSegments), force: true);
            }

            if (successfulExports == totalSegments)
            {
                ReportProgress(100, force: true);
                ExportStatus = $"Export complete: {successfulExports} file(s)";
                _messenger.Send(new LogMessage($"All segments ready: {successfulExports} file(s)."));
                _messenger.Send(new ExportFinishedMessage(true, cutDir));
            }
            else if (successfulExports > 0)
            {
                ExportStatus = $"Partial export: {successfulExports}/{totalSegments}";
                _messenger.Send(new LogMessage($"Export partially complete: {successfulExports}/{totalSegments}."));
                _messenger.Send(new ExportFinishedMessage(true, cutDir));
            }
            else if (_cts.IsCancellationRequested)
            {
                ExportStatus = "Export cancelled";
                _messenger.Send(new LogMessage("Export cancelled."));
                _messenger.Send(new ExportFinishedMessage(false));
            }
            else
            {
                ExportStatus = "Export failed";
                _messenger.Send(new LogMessage("Export failed."));
                _messenger.Send(new ExportFinishedMessage(false));
            }

            IsBusy = false;
            ExportProgress = 0;
            _cts.Dispose();
            _cts = null;
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(CanExport));
        }

        private void ReportProgress(int percent, bool force = false)
        {
            var now = DateTime.UtcNow;
            if (!force &&
                percent == _lastReportedProgress &&
                (now - _lastProgressUiUpdate).TotalMilliseconds < 150)
            {
                return;
            }

            _lastReportedProgress = percent;
            _lastProgressUiUpdate = now;
            ExportProgress = percent;
            _messenger.Send(new ExportProgressMessage(percent));
        }

        private async Task<bool> ExportSingleSegment(
            SegmentInfo seg,
            string outFile,
            CancellationToken token,
            IProgress<double> progress)
        {
            try
            {
                var startTime = seg.Start.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var endTime = seg.End.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var bitrateKbps = Math.Max(1500, (int)((_videoBitrate > 0 ? _videoBitrate : 4_000_000) / 1000));

                var args = $"-y -hide_banner -nostats -progress pipe:1 -ss {startTime} -to {endTime} -i \"{_currentFilePath}\" " +
                           $"-c:v libx264 -preset veryfast -b:v {bitrateKbps}k " +
                           $"-c:a aac -ar 48000 -vsync cfr -async 1 -reset_timestamps 1 " +
                           $"-movflags +faststart \"{outFile}\"";

                var success = await _ffmpeg.RunFfmpegAsync(args, seg.Duration, progress, token);
                if (!success && File.Exists(outFile)) File.Delete(outFile);
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
