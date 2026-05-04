# Как переключить окружение на Windows runner для сборки WPF (`dotnet build`)

Ниже — практический план, чтобы агент (и вы локально/в CI) могли **полноценно собирать UI-проект WPF** (`AI_Video_ToolKit.UI`) на .NET 8.

## Почему это нужно

WPF (`UseWPF=true`) поддерживается только на Windows. На Linux/macOS можно собрать `Domain/Core/Infrastructure`, но `AI_Video_ToolKit.UI` для `net8.0-windows` — только в Windows-среде.

## Шаг 1. Подготовить Windows среду

Варианты:

1. **Локально**: Windows 10/11 + PowerShell
2. **CI**: Windows runner (например, `windows-latest` в GitHub Actions)
3. **Self-hosted**: выделенная Windows VM/сервер под build

## Шаг 2. Установить .NET SDK 8

Проверьте наличие SDK:

```powershell
dotnet --list-sdks
```

Если `8.0.x` отсутствует — установите .NET 8 SDK:
- через официальный installer
- или через `winget`:

```powershell
winget install Microsoft.DotNet.SDK.8
```

Проверка:

```powershell
dotnet --info
```

## Шаг 3. Убедиться, что есть Desktop инструменты

Для WPF обычно достаточно .NET SDK на Windows, но на чистых машинах рекомендуется установить **Visual Studio Build Tools 2022** с компонентом .NET desktop build.

Минимум:
- MSBuild (из Build Tools/VS)
- .NET desktop tooling

Проверка `msbuild`:

```powershell
where msbuild
```

## Шаг 4. Открыть правильный каталог

```powershell
cd D:\AI_Video_ToolKit_next
```

Проверить `global.json` (если есть) — версия SDK должна совпадать/быть совместима.

## Шаг 5. Восстановление и сборка

```powershell
dotnet restore
dotnet build AI_Video_ToolKit_next.sln -c Debug
```

Для release:

```powershell
dotnet build AI_Video_ToolKit_next.sln -c Release
```

## Шаг 6. Тесты

```powershell
dotnet test AI_Video_ToolKit_next.sln -c Debug
```

Если тест-проектов нет — команда завершится корректно сообщением, что тестов не найдено.

## Шаг 7. Что делать, если build идёт в Linux контейнере

Если агент внутри Linux контейнера:
- не пытаться собирать `AI_Video_ToolKit.UI`
- вынести UI build в отдельный Windows job
- оставить Linux job для библиотек/линтинга/прочих задач

## Пример GitHub Actions (Windows job)

```yaml
name: build-windows-ui
on: [push, pull_request]

jobs:
  build-ui:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - name: Restore
        run: dotnet restore
      - name: Build
        run: dotnet build AI_Video_ToolKit_next.sln -c Debug --no-restore
      - name: Test
        run: dotnet test AI_Video_ToolKit_next.sln -c Debug --no-build
```

## Чек-лист «готово»

- [ ] `dotnet --list-sdks` показывает `8.0.x`
- [ ] runner/машина на Windows
- [ ] `dotnet restore` успешен
- [ ] `dotnet build ...` успешен
- [ ] `dotnet test ...` выполнен

