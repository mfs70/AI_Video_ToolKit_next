# 📘 GitHub + VSCode — Практическое руководство (для проекта AI_Video_ToolKit_next)

---

# 🧠 0. Базовая модель (понимание)

Всегда есть 3 состояния:

```text
Рабочая папка (файлы на диске)
→ Git (локальная история)
→ GitHub (удалённый репозиторий)
```

---

# 🚀 1. Клонирование репозитория (синхронизация старт)

## Через VSCode (рекомендуется)

1. `Ctrl + Shift + P`
2. Ввести:

```
Git: Clone
```

3. Вставить URL:

```
https://github.com/USER/REPO.git
```

4. Выбрать папку (например):

```
D:\AI_Video_ToolKit_next
```

5. Открыть проект

---

## Через CLI

```bash
git clone https://github.com/USER/REPO.git
cd REPO
code .
```

---

# 🔄 2. Синхронизация локальной папки и GitHub

## Основной цикл работы

```bash
git pull
→ редактируешь файлы
git add .
git commit -m "описание изменений"
git push
```

---

## В VSCode

1. Source Control (`Ctrl + Shift + G`)
2. Stage (кнопка `+`)
3. Ввести сообщение
4. Commit
5. Sync / Push

---

# ⚠️ КРИТИЧЕСКОЕ ПРАВИЛО

```text
ВСЕГДА → git pull перед началом работы
```

---

# 📁 3. Перемещение проекта на другой диск/папку

## ❗ Можно переносить БЕЗ проблем

Git хранит данные в:

```text
.git/
```

---

## Просто:

```text
Было:
D:\AI_Video_ToolKit_next

Стало:
H:\Projects\AI_Video_ToolKit_next
```

👉 НИЧЕГО ломаться не будет

---

## Проверка после переноса:

```bash
git status
```

---

## Проверка remote:

```bash
git remote -v
```

---

# 🔗 4. Привязка папки к GitHub (если её не было)

Если у тебя есть папка с проектом:

```bash
cd AI_Video_ToolKit_next

git init
git add .
git commit -m "initial commit"
git branch -M main
git remote add origin https://github.com/USER/REPO.git
git push -u origin main
```

---

# 🔀 5. Объединение репозиториев

## Сценарий:

👉 есть 2 проекта → нужно объединить

---

## Вариант 1 (простой — через копирование)

1. Скопировать файлы в один репозиторий
2. Выполнить:

```bash
git add .
git commit -m "merge projects"
git push
```

---

## Вариант 2 (правильный — через remote)

```bash
git remote add other https://github.com/USER/OTHER_REPO.git
git fetch other
git merge other/main
```

---

## ⚠️ Возможны конфликты

Git покажет:

```text
CONFLICT
```

👉 нужно вручную исправить файлы

---

# 🔄 6. Перенос проекта на другой компьютер

---

## Вариант A (правильный)

На новом ПК:

```bash
git clone https://github.com/USER/REPO.git
```

---

## Вариант B (через флешку)

1. Скопировать папку (вместе с `.git`)
2. Вставить на новый ПК

Проверка:

```bash
git status
```

---

## ⚠️ Если потерялся remote

```bash
git remote add origin https://github.com/USER/REPO.git
```

---

# 🔁 7. Работа с ветками (рекомендуется)

Создание ветки:

```bash
git checkout -b feature/timeline
```

Отправка:

```bash
git push origin feature/timeline
```

---

# 🧠 Почему это важно

```text
main → стабильная версия
feature → разработка
```

---

# ⚠️ 8. Частые ошибки

---

## ❌ Забыл pull

```text
→ конфликт при push
```

Решение:

```bash
git pull --rebase
```

---

## ❌ Коммит бинарников

```text
→ репозиторий раздувается
```

Решение:

`.gitignore`

---

## ❌ Потеря .git папки

```text
→ проект "забывает" историю
```

---

# 📦 9. Что НЕ должно попадать в Git

```text
C:\_Portable_
bin/
obj/
*.mp4
*.log
```

---

# 🧠 10. Рекомендуемый workflow

```text
1. git pull
2. работа
3. git add .
4. git commit
5. git push
```

---

# 🏁 Итог

Ты должен уметь:

✔ клонировать
✔ синхронизировать
✔ переносить проект
✔ объединять репозитории
✔ работать на разных ПК

---

# 🚀 Следующий уровень

* GitHub Actions (автосборка)
* версии (releases)
* CI/CD

---
