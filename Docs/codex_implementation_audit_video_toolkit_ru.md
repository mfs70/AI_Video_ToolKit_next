# Технический аудит и план внедрения функций для AI_Video_ToolKit_next

## Цели документа

Документ предназначен для:

- подготовки проекта к работе с Codex;
- минимизации потребления CPU/RAM/GPU/IO;
- полного внедрения MVVM + DI;
- устранения логических ошибок воспроизведения и таймлайна;
- подготовки системы монтажного стола;
- стандартизации hotkeys;
- подготовки чеклистов тестирования;
- выделения задач, которые можно выполнить без Codex;
- подготовки структуры проекта для дальнейшего масштабирования.

---

# 1. ОБЩИЙ ТЕХНИЧЕСКИЙ АУДИТ

## 1.1 Что необходимо получить перед работой

Для полноценного анализа потребуется содержимое следующих файлов.

### Обязательно запросить:

#### Core / App

- App.xaml
- App.xaml.cs
- MainWindow.xaml
- MainWindow.xaml.cs
- MainViewModel.cs

#### Player

- VideoPlayerService.cs
- TimelineControl.xaml
- TimelineControl.xaml.cs
- PlaybackService.cs
- FFmpegService.cs
- MediaEngineService.cs
- FrameAccurateSeekService.cs

#### Segments

- SegmentsPanel.xaml
- SegmentsPanelViewModel.cs
- MarkerModel.cs
- SegmentModel.cs

#### Playlist

- PlaylistViewModel.cs
- PlaylistControl.xaml
- PlaylistItem.cs

#### Export

- ExportService.cs
- EncodingService.cs
- QueueService.cs

#### DI / Architecture

- ServiceCollectionExtensions.cs
- Bootstrapper.cs
- Locator.cs
- Все файлы регистрации сервисов

#### Build

- *.csproj
- Directory.Build.props
- NuGet packages

#### FFmpeg

- Все команды FFmpeg
- Все шаблоны параметров
- Все вызовы Process.Start

---

# 2. КРИТИЧЕСКИЕ АРХИТЕКТУРНЫЕ ПРОБЛЕМЫ

## 2.1 Наиболее вероятные текущие проблемы

С высокой вероятностью проект страдает от:

- code-behind логики;
- отсутствия централизованного состояния playback;
- отсутствия single source of truth;
- прямого доступа UI к Player;
- race conditions таймлайна;
- таймеров DispatcherTimer вместо frame clock;
- отсутствия cancellation token;
- отсутствия event aggregation;
- множественных вызовов FFmpeg;
- блокировки UI потока;
- отсутствия virtualization;
- прямой мутации ObservableCollection;
- отсутствия command queue.

---

# 3. ПРЕДЕЛЬНАЯ ОПТИМИЗАЦИЯ (ОБЯЗАТЕЛЬНО)

## 3.1 Главный принцип

НЕЛЬЗЯ:

- декодировать видео повторно без необходимости;
- пересоздавать bitmap каждый кадр;
- обновлять UI чаще 30-60 fps;
- использовать polling там где возможны события;
- выполнять seek через полную перезагрузку файла;
- использовать sync FFmpeg calls;
- выполнять тяжёлые операции в UI thread.

НУЖНО:

- использовать shared frame cache;
- lazy loading;
- pooled buffers;
- immutable state где возможно;
- async pipelines;
- background queues;
- debouncing;
- coalescing UI updates.

---

# 4. ЗАДАЧИ КОТОРЫЕ МОЖНО СДЕЛАТЬ БЕЗ CODEX

# 4.1 HOTKEY SYSTEM

## Можно реализовать вручную

### Цель

Создать централизованную систему hotkeys.

---

## Архитектура

Создать:

- HotkeyService
- InputAction enum
- KeyBindingMap
- ICommand registry

---

## Пошагово

### Шаг 1

Создать enum:

```csharp
public enum InputAction
{
    PlayPause,
    Stop,
    NextFrame,
    PrevFrame,
    NextFile,
    PrevFile,
    ToggleAudio,
    ToggleVideo,
    ToggleLoop,
    ExportAll,
    Merge,
    Preview,
    MarkerIn,
    MarkerOut,
    MarkerCut,
    UndoMarker
}
```

---

### Шаг 2

Создать HotkeyService.

### Шаг 3

Перенести ВСЕ keybindings в один файл.

### Шаг 4

Полностью удалить обработку клавиатуры из code-behind.

---

## Чеклист

### Проверка

- [ ] хоткеи работают после mouse interaction
- [ ] хоткеи работают после seek
- [ ] хоткеи работают после drag&drop
- [ ] хоткеи не блокируются textbox
- [ ] хоткеи не дублируются
- [ ] нет утечек event handlers

---

# 4.2 FIX FRAME COUNTER STOP BUG

## Симптом

После stop:

- курсор уходит в 0
- frame counter остается на предыдущем кадре

---

## Причина

Вероятнее всего:

- UI обновляется отдельно от playback state;
- frame counter использует stale binding;
- stop вызывает seek без state update.

---

## Решение

Создать:

```csharp
PlaybackState
```

единый источник:

```csharp
CurrentFrame
CurrentTime
IsPlaying
PlaybackSpeed
```

STOP должен:

1. остановить playback;
2. обновить CurrentFrame=0;
3. вызвать timeline invalidate;
4. вызвать UI notify.

---

## Чеклист

- [ ] frame counter всегда совпадает с курсором
- [ ] stop работает во время playback
- [ ] stop работает на pause
- [ ] stop работает после seek
- [ ] stop работает при x16 speed
- [ ] stop работает при loop

---

# 4.3 MARKER SYSTEM

## Нужно полностью переработать

Текущая система вероятно:

- не имеет state machine;
- маркеры существуют отдельно от timeline;
- нет selected marker state.

---

## Правильная архитектура

### MarkerType

```csharp
In,
Out,
Cut
```

---

### MarkerModel

```csharp
Id
Frame
Type
IsSelected
```

---

## Ограничения

### Правила

- только один IN
- только один OUT
- CUT только между IN и OUT
- IN < CUT < OUT
- нельзя overlap

---

## Перемещение

### Mouse drag

Через:

```csharp
PointerPressed
PointerMoved
PointerReleased
```

---

### Keyboard

- left/right = 1 frame
- shift+left/right = 10 frames

---

## UI

Выбранный маркер:

- меняет цвет;
- показывает arrows;
- отображает номер кадра.

---

## Обновление сегментов

При движении marker:

обязательно:

- пересчитать segments;
- обновить segment titles;
- invalidate timeline;
- invalidate segment panel.

---

## Чеклист

- [ ] marker selection работает
- [ ] marker drag работает
- [ ] keyboard move работает
- [ ] shift move работает
- [ ] frame numbers обновляются
- [ ] segment names обновляются
- [ ] CUT невозможно вынести за IN/O
- [ ] marker delete работает
- [ ] undo marker работает

---

# 4.4 PLAYLIST SYSTEM

## Нужно реализовать:

- multi-select;
- drag&drop;
- auto-load;
- auto-play;
- keyboard navigation.

---

## Оптимизация

НЕ хранить:

- thumbnails full size;
- decoded frames.

Использовать:

- lazy thumbnails;
- thumbnail cache;
- background generation.

---

## Drag&Drop

### Использовать

```csharp
IDropTarget
IDragSource
```

или native Avalonia/WPF DnD.

---

## Чеклист

- [ ] multi-select работает
- [ ] ctrl-select работает
- [ ] shift-select работает
- [ ] delete работает
- [ ] next file autoload работает
- [ ] playlist empty state корректен
- [ ] drag multiple files работает
- [ ] drop to montage работает

---

# 4.5 LOGGER PANEL

## Нужно добавить

- collapse;
- save log;
- auto-scroll;
- severity filter.

---

## Оптимизация

НЕ использовать:

```csharp
TextBox.AppendText
```

Использовать:

- ring buffer;
- observable log entries;
- capped collection.

---

## Чеклист

- [ ] logger не тормозит UI
- [ ] logger не течет памятью
- [ ] save работает
- [ ] collapse работает
- [ ] export FFmpeg logs работает

---

# 5. ПРОБЛЕМЫ ВОСПРОИЗВЕДЕНИЯ

# 5.1 Preview Segment Bug

## Проблема

Preview проигрывает:

от marker start
до конца файла.

---

## Должно быть

Preview должен:

```text
segmentStart -> nextMarker
```

---

## Правильная логика

### Segment boundaries

```text
IN -> CUT1
CUT1 -> CUT2
CUT2 -> OUT
```

---

## Loop

Loop должен:

```text
seek(segmentStart)
play again
```

без reload media.

---

## Критично

НЕЛЬЗЯ:

- выполнять reload file;
- пересоздавать decoder;
- пересоздавать audio device.

---

## Чеклист

- [ ] preview играет только segment
- [ ] loop работает
- [ ] no-loop останавливается
- [ ] preview respects IN/O
- [ ] playback seamless

---

# 5.2 Audio/Video Toggle

## Симптом

Галочки не работают.

---

## Возможные причины

- flags не передаются в FFmpeg;
- playback engine игнорирует mute state;
- export pipeline не использует settings.

---

## Должно быть

### Audio OFF

Playback:

```text
mute audio renderer
```

Export:

```bash
-an
```

---

### Video OFF

Playback:

```text
hide renderer
```

Export:

```bash
-vn
```

---

## Чеклист

- [ ] playback audio toggle работает
- [ ] export audio toggle работает
- [ ] playback video toggle работает
- [ ] export video toggle работает
- [ ] both off disables export

---

# 5.3 Playback Speed

## Требование

До x2:

- звук воспроизводится.

После x2:

- звук отключается.

---

## Оптимизация

НЕ использовать pitch correction.

Слишком дорого.

---

## Реализация

```text
speed <= 2.0 => audio enabled
speed > 2.0 => mute audio
```

---

## Чеклист

- [ ] x0.1 работает
- [ ] x0.25 работает
- [ ] x0.5 работает
- [ ] x2 audio ON
- [ ] x4 audio OFF
- [ ] no desync

---

# 6. MKV AUDIO DESYNC

## Проблема

Audio longer than video.

---

## Причины

Очень вероятно:

- неправильный trim;
- timestamp drift;
- VFR source;
- bad concat;
- incorrect stream copy.

---

## Критично

Для frame-accurate editing:

НЕ использовать:

```bash
-c copy
```

на mkv VFR.

---

## Нужно

Всегда:

```bash
-vsync cfr
```

и:

```bash
-reset_timestamps 1
```

---

## Рекомендуемый pipeline

### Decode/reencode safe mode

```bash
ffmpeg -i input.mkv \
-r 30 \
-vsync cfr \
-c:v libx264 \
-c:a aac \
-reset_timestamps 1
```

---

## Чеклист

- [ ] audio length == video length
- [ ] no drift after export
- [ ] concat sync correct
- [ ] VFR sources correct
- [ ] frame count preserved

---

# 7. МОНТАЖНЫЙ СТОЛ

# 7.1 Архитектура

## Нужны модели

### MontageClip

```csharp
Id
Path
StartFrame
EndFrame
DisplayName
Order
Thumbnail
```

---

### MontageTrack

```csharp
ObservableCollection<MontageClip>
```

---

# 7.2 Визуальное представление

## Каждый clip

- rectangle;
- thumbnail;
- clip number;
- frame range.

---

## Оптимизация

НЕ рендерить:

- full thumbnails;
- live video preview.

Использовать:

- cached jpg preview.

---

# 7.3 Магнитное сцепление

## Правило

Все клипы:

```text
clip[n].end == clip[n+1].start
```

визуально.

---

## Drag logic

После drag:

- reorder collection;
- recompute positions;
- snap neighbors.

---

# 7.4 Merge pipeline

## НЕЛЬЗЯ

Делать merge через:

```bash
filter_complex concat
```

для сотен файлов.

---

## Лучше

Использовать concat demuxer.

### concat.txt

```text
file '001.mp4'
file '002.mp4'
```

---

### FFmpeg

```bash
ffmpeg -f concat -safe 0 -i concat.txt -c copy out.mp4
```

---

## Если codecs mismatch

Тогда:

- normalize first;
- then concat.

---

## Чеклист

- [ ] clips draggable
- [ ] reorder works
- [ ] delete works
- [ ] magnetic snapping works
- [ ] merge works
- [ ] output sync correct
- [ ] output duration correct

---

# 8. EXPORT SYSTEM

# 8.1 ExportAll Progress

## Проблема

ProgressBar не связан.

---

## Нужно

Создать:

```csharp
ExportJob
```

### Поля

```csharp
Progress
Status
CurrentFile
Elapsed
```

---

## FFmpeg progress

Использовать:

```bash
-progress pipe:1
```

---

## Парсить:

```text
out_time_ms
progress
fps
speed
```

---

## Чеклист

- [ ] progress updates live
- [ ] logger shows command
- [ ] cancel works
- [ ] queue works
- [ ] no UI freeze

---

# 9. CROP SYSTEM

# 9.1 UI

## Нужно

Overlay:

- live crop rectangle;
- resize handles;
- dimensions overlay.

---

## Оптимизация

НЕ crop preview video realtime.

Слишком дорого.

Использовать:

- overlay only.

---

# 9.2 Export

### FFmpeg

```bash
-vf crop=w:h:x:y
```

---

## Чеклист

- [ ] crop handles work
- [ ] keyboard move works
- [ ] dimensions update
- [ ] export crop correct
- [ ] aspect ratio preserved if needed

---

# 10. FRAME EXTRACT / ASSEMBLE

# 10.1 Разбор в кадры

## FFmpeg

```bash
ffmpeg -i input.mp4 Frames/frame_%06d.png
```

---

# 10.2 Сборка

```bash
ffmpeg -framerate 30 -i frame_%06d.png out.mp4
```

---

## Оптимизация

PNG очень тяжелый.

Для скорости:

```text
jpg quality 2-4
```

---

## Чеклист

- [ ] extraction works
- [ ] numbering preserved
- [ ] assemble works
- [ ] fps preserved
- [ ] audio optional

---

# 11. MVVM + DI

# 11.1 ОБЯЗАТЕЛЬНОЕ ВНЕДРЕНИЕ

## Сейчас вероятно:

- service locator;
- singleton chaos;
- code-behind events;
- direct UI references.

---

# 11.2 Что нужно сделать

## View

ТОЛЬКО:

- bindings;
- visual states.

---

## ViewModel

ТОЛЬКО:

- commands;
- state;
- orchestration.

---

## Services

ТОЛЬКО:

- IO;
- playback;
- ffmpeg;
- encoding;
- filesystem.

---

# 11.3 DI

## Использовать

```text
Microsoft.Extensions.DependencyInjection
```

---

## Lifetime

### Singleton

- PlaybackService
- FFmpegService
- LoggerService

### Scoped/Transient

- dialogs
- temporary jobs

---

# 11.4 Event System

## НЕЛЬЗЯ

Использовать:

```text
ViewModel -> View direct calls
```

---

## Использовать

- messenger;
- event aggregator;
- reactive streams.

---

## Чеклист

- [ ] no business logic in View
- [ ] no static services
- [ ] all services via DI
- [ ] commands centralized
- [ ] no UI thread blocking
- [ ] testable viewmodels

---

# 12. ЧТО НУЖНО СДЕЛАТЬ ДО CODEX

# КРИТИЧЕСКИ ВАЖНО

## Сначала вручную:

### Этап 1

- [ ] собрать структуру проекта
- [ ] убрать dead code
- [ ] убрать дубли сервисов
- [ ] убрать code-behind playback logic
- [ ] внедрить centralized playback state

---

### Этап 2

- [ ] внедрить DI
- [ ] внедрить logger
- [ ] внедрить command system
- [ ] внедрить hotkey service

---

### Этап 3

- [ ] стабилизировать timeline
- [ ] стабилизировать marker system
- [ ] стабилизировать playback

---

### Этап 4

- [ ] только после этого подключать Codex
- [ ] только после этого автоматизировать refactoring
- [ ] только после этого генерировать новые модули

---

# 13. ЧТО НУЖНО ПРОВЕРИТЬ В ПЕРВУЮ ОЧЕРЕДЬ

## Критические проверки

### Playback

- [ ] frame accurate seek
- [ ] no drift
- [ ] no memory leak
- [ ] no decoder recreation

---

### FFmpeg

- [ ] commands logged
- [ ] no orphan processes
- [ ] cancellation works
- [ ] progress works

---

### UI

- [ ] no UI freeze
- [ ] virtualization works
- [ ] drag/drop smooth
- [ ] timeline smooth

---

### Memory

- [ ] bitmap disposal
- [ ] thumbnail cache bounded
- [ ] no unmanaged leak
- [ ] no event leak

---

# 14. СЛЕДУЮЩИЙ ЭТАП

Для продолжения анализа необходимо содержимое:

1. MainWindow.xaml
2. MainViewModel.cs
3. PlaybackService.cs
4. TimelineControl.xaml.cs
5. FFmpegService.cs
6. SegmentsPanelViewModel.cs
7. PlaylistViewModel.cs
8. ExportService.cs
9. все FFmpeg команды
10. структура проекта

После получения файлов можно:

- подготовить конкретные патчи;
- подготовить Codex-ready prompts;
- построить dependency graph;
- выявить bottlenecks;
- подготовить migration plan;
- подготовить performance budget;
- построить полноценную архитектуру монтажного стола.

