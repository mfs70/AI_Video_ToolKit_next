// Файл: ViewModels/PlaylistViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Win32;                          // Для OpenFileDialog
using AI_Video_ToolKit.UI.Messages;           // Сообщения для шины

namespace AI_Video_ToolKit.UI.ViewModels
{
    /// <summary>
    /// ViewModel для управления плейлистом.
    /// Позволяет добавлять, удалять, очищать элементы и переключаться между ними.
    /// </summary>
    public partial class PlaylistViewModel : ObservableObject
    {
        private readonly IMessenger _messenger;

        /// <summary>
        /// Коллекция элементов плейлиста. Автоматически уведомляет UI об изменениях.
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<PlaylistItem> _items = new();

        /// <summary>
        /// Текущий выбранный элемент плейлиста.
        /// </summary>
        [ObservableProperty]
        private PlaylistItem? _selectedItem;

        public PlaylistViewModel(IMessenger messenger)
        {
            _messenger = messenger;
        }

        /// <summary>
        /// Команда: открыть диалог выбора файлов и добавить выбранные медиа в плейлист.
        /// Если плейлист был пуст, сразу загружается первый добавленный файл.
        /// </summary>
        [RelayCommand]
        private void AddFiles()
        {
            var dlg = new OpenFileDialog
            {
                // Допустимые расширения (видео и изображения)
                Filter = "Media files|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.jpg;*.jpeg;*.png;*.bmp;*.gif",
                Multiselect = true
            };

            if (dlg.ShowDialog() == true)
            {
                bool wasEmpty = Items.Count == 0;
                PlaylistItem? firstAdded = null;

                foreach (var path in dlg.FileNames)
                {
                    if (AddToPlaylist(path) && firstAdded == null)
                        firstAdded = Items.LastOrDefault(); // запоминаем первый реально добавленный
                }

                // Если до этого плейлист был пуст и что-то добавилось — выбираем первый и загружаем его
                if (wasEmpty && firstAdded != null)
                {
                    SelectedItem = firstAdded;
                    _messenger.Send(new LoadFileMessage(firstAdded.FilePath));
                }
            }
        }

        /// <summary>
        /// Команда: удалить выбранный элемент из плейлиста.
        /// После удаления выбирается соседний элемент (или никакой) и сообщает плееру о загрузке.
        /// </summary>
        [RelayCommand]
        private void RemoveSelected()
        {
            if (SelectedItem == null) return;

            int index = Items.IndexOf(SelectedItem);
            Items.Remove(SelectedItem);

            if (Items.Count > 0)
            {
                // Выбираем следующий элемент или предыдущий, если удаляли последний
                int newIndex = (index < Items.Count) ? index : Items.Count - 1;
                SelectedItem = Items[newIndex];
                _messenger.Send(new LoadFileMessage(SelectedItem.FilePath));
            }
            else
            {
                SelectedItem = null;
                // Здесь можно отправить сообщение остановки воспроизведения, если понадобится
            }
        }

        /// <summary>
        /// Команда: полностью очистить плейлист.
        /// Воспроизведение не останавливается (оставлено на усмотрение пользователя).
        /// </summary>
        [RelayCommand]
        private void Clear()
        {
            Items.Clear();
            SelectedItem = null;
        }

        /// <summary>
        /// Команда: перейти к следующему элементу плейлиста (циклически).
        /// Отправляет сообщение для загрузки нового файла в плеер.
        /// </summary>
        [RelayCommand]
        private void MoveNext()
        {
            if (Items.Count == 0 || SelectedItem == null) return;

            int index = Items.IndexOf(SelectedItem);
            int newIndex = (index + 1) % Items.Count;   // циклический переход
            SelectedItem = Items[newIndex];
            _messenger.Send(new LoadFileMessage(SelectedItem.FilePath));
        }

        /// <summary>
        /// Команда: перейти к предыдущему элементу плейлиста (циклически).
        /// Отправляет сообщение для загрузки нового файла в плеер.
        /// </summary>
        [RelayCommand]
        private void MovePrevious()
        {
            if (Items.Count == 0 || SelectedItem == null) return;

            int index = Items.IndexOf(SelectedItem);
            int newIndex = (index - 1 + Items.Count) % Items.Count; // циклический переход назад
            SelectedItem = Items[newIndex];
            _messenger.Send(new LoadFileMessage(SelectedItem.FilePath));
        }

        /// <summary>
        /// Попытаться добавить файл в плейлист с проверками: существование, поддерживаемое расширение, отсутствие дубликата.
        /// Возвращает true, если файл действительно добавлен.
        /// </summary>
        public bool AddToPlaylist(string path)
        {
            if (!File.Exists(path)) return false;

            var ext = Path.GetExtension(path).ToLower();
            if (!IsSupported(ext)) return false;

            // Не добавляем дубликат (сравнение без учёта регистра)
            if (Items.Any(item => string.Equals(item.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                return false;

            Items.Add(new PlaylistItem { FilePath = path });
            return true;
        }

        /// <summary>
        /// Проверяет, входит ли расширение в список поддерживаемых.
        /// </summary>
        private static bool IsSupported(string ext) =>
            ext is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm"
                or ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif";
    }
}