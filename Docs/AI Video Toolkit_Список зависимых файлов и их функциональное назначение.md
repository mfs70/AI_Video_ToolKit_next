# AI Video Toolkit - Список зависимых файлов и их функциональное назначение

## 📁 Основные файлы проекта

### 1. Файлы интерфейса (UI)

| Файл | Расположение | Назначение | Зависит от |
|------|-------------|------------|------------|
| **MainWindow.xaml** | `AI_Video_ToolKit.UI/` | Главное окно приложения. Содержит всю разметку интерфейса: видео превью, панель управления, таймлайн, список сегментов, логгер. | VideoPreviewControl, TimelineControl |
| **MainWindow.xaml.cs** | `AI_Video_ToolKit.UI/` | Code-behind файл главного окна. Обрабатывает все события: загрузка видео, управление воспроизведением, создание отрезков, экспорт. | FFmpegService, VideoPlayerService |
| **App.xaml** | `AI_Video_ToolKit.UI/` | Точка входа приложения. Определяет глобальные ресурсы и стили. | - |
| **App.xaml.cs** | `AI_Video_ToolKit.UI/` | Логика запуска приложения, обработка необработанных исключений. | - |

### 2. Пользовательские элементы управления (Controls)

| Файл | Расположение | Назначение | Зависит от |
|------|-------------|------------|------------|
| **VideoPreviewControl.xaml** | `AI_Video_ToolKit.UI/Controls/` | Элемент управления для отображения видео. Содержит MediaElement и элементы управления воспроизведением. | - |
| **VideoPreviewControl.xaml.cs** | `AI_Video_ToolKit.UI/Controls/` | Логика воспроизведения видео: Play, Pause, Stop, перемотка, регулировка скорости. | VideoPlayerService |
| **TimelineControl.xaml** | `AI_Video_ToolKit.UI/Controls/` | Визуальная временная шкала. Отображает маркеры отрезков, текущую позицию. | - |
| **TimelineControl.xaml.cs** | `AI_Video_ToolKit.UI/Controls/` | Логика работы таймлайна: добавление/удаление маркеров, навигация по клику, масштабирование. | SegmentManager |

### 3. Сервисы (Services)

| Файл | Расположение | Назначение | Зависит от |
|------|-------------|------------|------------|
| **FFmpegService.cs** | `AI_Video_ToolKit.Core/Services/` | Работа с FFmpeg. Вырезание отрезков, объединение видео, экспорт, получение метаданных. | FFmpeg.exe |
| **VideoPlayerService.cs** | `AI_Video_ToolKit.Core/Services/` | Управление воспроизведением видео через FFmpeg или MediaElement. | FFmpegService |
| **SegmentManager.cs** | `AI_Video_ToolKit.Core/Services/` | Управление отрезками видео: создание, удаление, хранение, сортировка. | - |
| **ExportService.cs** | `AI_Video_ToolKit.Core/Services/` | Экспорт видео в различные форматы. Обработка прогресса экспорта. | FFmpegService |
| **FileDialogService.cs** | `AI_Video_ToolKit.Core/Services/` | Работа с диалогами открытия/сохранения файлов. | - |
| **LoggerService.cs** | `AI_Video_ToolKit.Core/Services/` | Логирование событий приложения в файл и интерфейс. | - |

### 4. Модели данных (Models)

| Файл | Расположение | Назначение |
|------|-------------|------------|
| **VideoInfo.cs** | `AI_Video_ToolKit.Core/Models/` | Модель с информацией о видео: путь, разрешение, FPS, кодек, битрейт, длительность. |
| **Segment.cs** | `AI_Video_ToolKit.Core/Models/` | Модель отрезка видео: время начала, время конца, длительность, имя файла. |
| **ExportSettings.cs** | `AI_Video_ToolKit.Core/Models/` | Настройки экспорта: формат, качество, кодек, битрейт. |
| **ProjectFile.cs** | `AI_Video_ToolKit.Core/Models/` | Модель для сохранения проекта: все отрезки, настройки, метаданные. |

### 5. Helpers (Вспомогательные классы)

| Файл | Расположение | Назначение |
|------|-------------|------------|
| **TimeConverter.cs** | `AI_Video_ToolKit.Core/Helpers/` | Конвертация времени между форматами (TimeSpan, миллисекунды, кадры). |
| **FileHelper.cs` | `AI_Video_ToolKit.Core/Helpers/` | Вспомогательные функции для работы с файлами: проверка существования, получение размера. |
| **ValidationHelper.cs** | `AI_Video_ToolKit.Core/Helpers/` | Валидация входных данных: проверка путей, форматов, диапазонов. |

### 6. ViewModels (MVVM, если используется)

| Файл | Расположение | Назначение |
|------|-------------|------------|
| **MainViewModel.cs** | `AI_Video_ToolKit.UI/ViewModels/` | ViewModel для главного окна. Содержит команды и свойства для привязки. |
| **SegmentViewModel.cs** | `AI_Video_ToolKit.UI/ViewModels/` | ViewModel для отображения отрезков в списке. |

### 7. Конвертеры (Converters)

| Файл | Расположение | Назначение |
|------|-------------|------------|
| **TimeToStringConverter.cs** | `AI_Video_ToolKit.UI/Converters/` | Конвертер TimeSpan в строку для отображения. |
| **BoolToVisibilityConverter.cs** | `AI_Video_ToolKit.UI/Converters/` | Конвертер bool в Visibility (Visible/Collapsed). |
| **ProgressToPercentConverter.cs` | `AI_Video_ToolKit.UI/Converters/` | Конвертер значения прогресса в проценты для отображения. |

---

## 📁 Структура проекта
