# ⚙️ .NET SDK 8 — ПОЛНОЕ РУКОВОДСТВО (CLI)

## 📌 Назначение

Этот документ — **практическая инструкция** по работе с .NET SDK 8 через командную строку:

* создание проектов
* сборка и запуск
* очистка и перестроение
* работа с решениями (solution)
* диагностика проблем

---

# 🧱 1. ПРОВЕРКА УСТАНОВКИ

```bash
dotnet --version
```

Ожидаемо:

```text
8.0.xxx
```

---

```bash
dotnet --info
```

Показывает:

* SDK версии
* Runtime
* пути установки

---

# 🆕 2. СОЗДАНИЕ ПРОЕКТОВ

---

## 📦 Создать WPF приложение

```bash
dotnet new wpf -n MyApp
```

---

## 📦 Создать библиотеку

```bash
dotnet new classlib -n MyLibrary
```

---

## 📦 Создать консольное приложение

```bash
dotnet new console -n MyConsole
```

---

## 📂 Перейти в проект

```bash
cd MyApp
```

---

# 🧩 3. РАБОТА С РЕШЕНИЕМ (SOLUTION)

---

## ➕ Создать solution

```bash
dotnet new sln -n MySolution
```

---

## ➕ Добавить проект

```bash
dotnet sln add MyApp/MyApp.csproj
```

---

## ➕ Добавить несколько проектов

```bash
dotnet sln add **/*.csproj
```

---

## 🔗 Добавить зависимость (ссылка)

```bash
dotnet add MyApp reference MyLibrary
```

---

# 🔨 4. СБОРКА ПРОЕКТА

---

## ▶️ Обычная сборка

```bash
dotnet build
```

---

## ▶️ Сборка конкретного проекта

```bash
dotnet build MyApp.csproj
```

---

## ▶️ Сборка в Release

```bash
dotnet build -c Release
```

---

## 📦 Результат

```text
bin/Debug/net8.0/
bin/Release/net8.0/
```

---

# 🚀 5. ЗАПУСК ПРОЕКТА

---

```bash
dotnet run
```

---

## ▶️ С указанием проекта

```bash
dotnet run --project MyApp
```

---

## ▶️ Release запуск

```bash
dotnet run -c Release
```

---

# 🧹 6. ОЧИСТКА ПРОЕКТА

---

## ❌ Удалить временные файлы

```bash
dotnet clean
```

Удаляет:

```text
bin/
obj/
```

---

# 🔁 7. ПЕРЕСТРОЕНИЕ (REBUILD)

---

❗ В .NET CLI нет команды `rebuild`

👉 аналог:

```bash
dotnet clean
dotnet build
```

---

# 🔄 8. ВОССТАНОВЛЕНИЕ ПАКЕТОВ

---

```bash
dotnet restore
```

Обычно выполняется автоматически при build

---

# 📦 9. УПРАВЛЕНИЕ ПАКЕТАМИ

---

## ➕ Установить пакет

```bash
dotnet add package Newtonsoft.Json
```

---

## ❌ Удалить пакет

```bash
dotnet remove package Newtonsoft.Json
```

---

## 📜 Список пакетов

```bash
dotnet list package
```

---

# 🧪 10. ПУБЛИКАЦИЯ (DEPLOY)

---

## 📦 Опубликовать

```bash
dotnet publish -c Release -o publish
```

---

## 📁 Результат

```text
publish/
```

---

## 🧱 Self-contained (без .NET)

```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

---

# ⚠️ 11. ТИПИЧНЫЕ ОШИБКИ

---

## ❌ "файл занят процессом"

```text
MSB3021 / MSB3027
```

### ✔ Решение:

```bash
taskkill /IM MyApp.exe /F
```

---

## ❌ "invalid JSON global.json"

### ✔ Решение:

* удалить или исправить `global.json`

---

## ❌ "SDK not found"

```bash
dotnet --list-sdks
```

---

## ❌ "пакеты не восстановлены"

```bash
dotnet restore
```

---

# 🧠 12. ПОЛЕЗНЫЕ КОМАНДЫ

---

## 📜 Список SDK

```bash
dotnet --list-sdks
```

---

## 📜 Список runtime

```bash
dotnet --list-runtimes
```

---

## 🔍 Проверка шаблонов

```bash
dotnet new list
```

---

## 🔄 Обновить шаблоны

```bash
dotnet new update
```

---

# 🧩 13. РАБОТА С ПУТЯМИ

---

## ❗ Важно

Используй:

```csharp
Path.Combine()
```

НЕ:

```csharp
"folder/file"
```

---

# 🧱 14. СТРУКТУРА ПРОЕКТА

---

```text
project/
│
├── bin/        # сборка
├── obj/        # временные файлы
├── *.csproj    # проект
```

---

# 🚀 15. РЕКОМЕНДУЕМЫЙ WORKFLOW

---

```bash
dotnet restore
dotnet build
dotnet run
```

---

## 🔁 При проблемах

```bash
dotnet clean
dotnet restore
dotnet build
```

---

# 🏁 ИТОГ

---

## Ты умеешь:

✔ создавать проекты
✔ управлять solution
✔ собирать и запускать
✔ чистить и восстанавливать
✔ публиковать

---

## 💡 Главное правило

```text
ВСЕГДА:
clean → restore → build
```

---

## 🎯 Следующий уровень

* MSBuild (глубокая настройка)
* CI/CD
* Docker
