using System;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using AI_Video_ToolKit.UI.Services;

namespace AI_Video_ToolKit.UI
{
    public partial class MainWindow : Window
    {
        private string? _file;
        private string? _audioFile;

        private readonly BufferedVideoPlayer _player = new();
        private readonly AudioPlayerService _audio = new();
        private readonly AudioExtractorService _extractor = new();
        private readonly FFprobeService _ffprobe = new();

        private int _width;
        private int _height;
        private double _fps;
        private bool _hasAudio;

        private CancellationTokenSource? _seekCts;

        public MainWindow()
        {
            InitializeComponent();

            _player.OnFrame += frame =>
            {
                Dispatcher.Invoke(() => Preview.SetFrame(frame));
            };

            Timeline.OnTimeChanged += Timeline_OnTimeChanged;
        }

        private async void Load_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog();

                if (dlg.ShowDialog() != true)
                    return;

                _file = dlg.FileName;

                var info = await _ffprobe.GetInfo(_file);

                _width = info.width;
                _height = info.height;
                _fps = info.fps;
                _hasAudio = info.hasAudio;

                Timeline.SetDuration(info.duration);

                if (_hasAudio)
                {
                    _audioFile = await _extractor.Extract(_file);
                }
                else
                {
                    _audioFile = null;
                }

                StartPlayback(TimeSpan.Zero);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "LOAD ERROR");
            }
        }

        private void StartPlayback(TimeSpan time)
        {
            try
            {
                if (_file == null) return;

                if (_hasAudio && _audioFile != null)
                {
                    _audio.Play(_audioFile, time.TotalSeconds);
                }

                _player.Start(
                    _file,
                    _width,
                    _height,
                    _fps,
                    time,
                    () => _hasAudio ? _audio.GetPosition() : time.TotalSeconds,
                    () => _audio.Pause(),
                    () => _audio.Resume()
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "PLAYBACK ERROR");
            }
        }

        private void Timeline_OnTimeChanged(TimeSpan time)
        {
            if (_file == null) return;

            _seekCts?.Cancel();
            _seekCts = new CancellationTokenSource();

            var token = _seekCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(120, token);

                    if (!token.IsCancellationRequested)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StartPlayback(time);
                        });
                    }
                }
                catch { }
            });
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            _player.Stop();
            _audio.Stop();
        }
    }
}