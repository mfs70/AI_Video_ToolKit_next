// Файл: AI_Video_ToolKit.UI/ViewModels/MainViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using AI_Video_ToolKit.Infrastructure.Services;
using AI_Video_ToolKit.UI.Services;

namespace AI_Video_ToolKit.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly BufferedVideoPlayer _player;
        private readonly FFprobeService _ffprobe;
        private readonly FrameGrabber _grabber;

        [ObservableProperty]
        private string _statusText = "✅ Ready";

        [ObservableProperty]
        private string _currentFile = "";

        [ObservableProperty]
        private string _currentFileName = "";

        [ObservableProperty]
        private double _exportProgress;

        [ObservableProperty]
        private bool _isPlaying;

        private readonly double[] _speeds = { 0.1, 0.25, 0.5, 1, 2, 4, 8, 16 };
        private int _speedIndex = 3;

        public double Speed => _speeds[_speedIndex];

        [ObservableProperty]
        private int _selectedSpeedIndex = 3;

        partial void OnSelectedSpeedIndexChanged(int value)
        {
            if (value >= 0 && value < _speeds.Length)
            {
                _speedIndex = value;
                OnPropertyChanged(nameof(Speed));
                if (Application.Current.MainWindow is MainWindow mw)
                {
                    mw.RestartPlaybackWithNewSpeed();
                }
            }
        }

        public ObservableCollection<string> LogEntries { get; } = new();

        [RelayCommand]
        private Task LoadFiles()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Media files|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.jpg;*.jpeg;*.png;*.bmp;*.gif",
                Multiselect = true
            };
            if (dlg.ShowDialog() == true)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Application.Current.MainWindow is MainWindow mw)
                    {
                        mw.LoadFilesToPlaylist(dlg.FileNames);
                        if (mw.PlaylistItemCount > 0 && string.IsNullOrEmpty(mw.CurrentFilePath))
                        {
                            _ = mw.LoadFile(dlg.FileNames[0]);
                        }
                    }
                });
            }
            return Task.CompletedTask;
        }

        [RelayCommand]
        private void PlayPause()
        {
            if (Application.Current.MainWindow is MainWindow mw)
            {
                mw.TogglePlayPause();
            }
        }

        [RelayCommand]
        private void Stop()
        {
            if (Application.Current.MainWindow is MainWindow mw)
            {
                mw.StopPlayback();
            }
        }

        [RelayCommand]
        private void Previous()
        {
            if (Application.Current.MainWindow is MainWindow mw)
            {
                mw.PreviousPlaylistItem();
            }
        }

        [RelayCommand]
        private void Next()
        {
            if (Application.Current.MainWindow is MainWindow mw)
            {
                mw.NextPlaylistItem();
            }
        }

        public MainViewModel(BufferedVideoPlayer player, FFprobeService ffprobe, FrameGrabber grabber)
        {
            _player = player;
            _ffprobe = ffprobe;
            _grabber = grabber;
        }

        public void AddLog(string text)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] {text}");
                if (LogEntries.Count > 500) LogEntries.RemoveAt(0);
            });
        }
    }
}