// Файл: AI_Video_ToolKit.UI/ViewModels/MainViewModel.cs
using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

        public ObservableCollection<string> LogEntries { get; } = new();

        [RelayCommand]
        private async Task LoadFiles()
        {
            StatusText = "Loading...";
            // В будущем здесь будет полноценная логика загрузки.
        }

        public MainViewModel(BufferedVideoPlayer player, FFprobeService ffprobe, FrameGrabber grabber)
        {
            _player = player;
            _ffprobe = ffprobe;
            _grabber = grabber;
        }

        public void AddLog(string text)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] {text}");
                if (LogEntries.Count > 500) LogEntries.RemoveAt(0);
            });
        }
    }
}