# 🎬 AI_Video_ToolKit_next

GUI-first инструмент для работы с видео на базе FFmpeg.

---

## 🚀 Возможности

- 🎥 Встроенный видеоплеер (WPF)
- ⏯ Play / Pause / Stop
- ⏱ Timeline + перемотка
- 📊 ProgressBar при кодировании
- 🧠 FFprobe анализ
- 🎬 FFmpeg encoding
- 📁 Выбор входного файла и выходной папки
- 📝 Live лог выполнения

---

## 🧱 Архитектура
AI_Video_ToolKit_next/
│
├── AI_Video_ToolKit.UI/ # WPF GUI
├── app/ # PowerShell toolkit (будет)
├── scripts/ # вспомогательные скрипты
├── docs/ # документация
└── README.md


---

## ⚙️ Зависимости

Все бинарники находятся отдельно:
C:_Portable_\

### Требуется:

- ffmpeg
- ffprobe
- ffplay

Пример:
C:_Portable_\ffmpeg\bin\ffmpeg.exe

---

## ▶️ Запуск

```bash
dotnet build
dotnet run



📦 Как использовать
1. Выбрать входной файл

Browse → выбрать видео

2. Выбрать папку вывода

Browse → выбрать папку

3. Preview

Просмотр видео внутри приложения

4. Encode

Кодирование:

файл сохраняется в выбранную папку
имя генерируется автоматически

📊 Прогресс
отображается % выполнения
основан на времени (time= из FFmpeg)

⚠️ Ограничения
MediaElement зависит от кодеков Windows
рекомендуется использовать H.264

🧠 Roadmap
 STOP encoding
 ETA (оставшееся время)
 скорость кодирования
 presets (1080p / 4K)
 интеграция Real-ESRGAN
 интеграция RIFE
 PowerShell pipeline