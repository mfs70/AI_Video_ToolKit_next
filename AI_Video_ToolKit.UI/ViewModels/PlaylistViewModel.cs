using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Win32;
using AI_Video_ToolKit.UI.Messages; // создадим папку Messages позже

namespace AI_Video_ToolKit.UI.ViewModels
{
    public partial class PlaylistViewModel : ObservableObject
    {
        private readonly IMessenger _messenger;

        [ObservableProperty]
        private ObservableCollection<PlaylistItem> _items = new();

        [ObservableProperty]
        private PlaylistItem? _selectedItem;

        public PlaylistViewModel(IMessenger messenger)
        {
            _messenger = messenger;
        }

        [RelayCommand]
        private void AddFiles()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Media files|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.jpg;*.jpeg;*.png;*.bmp;*.gif",
                Multiselect = true
            };
            if (dlg.ShowDialog() == true)
            {
                PlaylistItem? firstAdded = null;
                foreach (var path in dlg.FileNames)
                {
                    if (AddToPlaylist(path) && firstAdded == null)
                        firstAdded = Items.LastOrDefault();
                }
                if (firstAdded != null && SelectedItem == null)
                {
                    SelectedItem = firstAdded;
                    _messenger.Send(new LoadFileMessage(firstAdded.FilePath));
                }
            }
        }

        [RelayCommand]
        private void RemoveSelected()
        {
            if (SelectedItem != null)
                Items.Remove(SelectedItem);
        }

        [RelayCommand]
        private void Clear()
        {
            Items.Clear();
        }

        public bool AddToPlaylist(string path)
        {
            if (!File.Exists(path)) return false;
            var ext = Path.GetExtension(path).ToLower();
            if (!IsSupported(ext)) return false;

            if (Items.Any(item => string.Equals(item.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                return false;

            Items.Add(new PlaylistItem { FilePath = path });
            return true;
        }

        private static bool IsSupported(string ext) =>
            ext is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm"
                or ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif";
    }
}