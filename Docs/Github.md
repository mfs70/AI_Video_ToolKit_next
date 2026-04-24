# 📘 GitHub + VSCode — Практическое руководство (для проекта AI_Video_ToolKit_next)

---

# 🧠 0. Базовая модель (понимание)

Всегда есть 3 состояния:

```
Рабочая папка (файлы на диске)
→ Git (локальная история)
→ GitHub (удалённый репозиторий)
```

---

# 🚀 1. Клонирование репозитория

## VSCode

1. `Ctrl + Shift + P`
2. `Git: Clone`
3. Вставить URL
4. Выбрать папку
5. Open

---

## CLI

```bash
git clone https://github.com/USER/REPO.git
cd REPO
code .
```

---

# 🔄 2. Синхронизация (основной цикл)

```bash
git pull
→ работаешь с файлами
git add .
git commit -m "описание"
git push
```

---

# ⚠️ Правило

```
ВСЕГДА делай git pull перед началом работы
```

---

# 📁 3. Перемещение проекта

Можно свободно переносить:

```
D:\AI_Video_ToolKit_next → H:\Projects\
```

✔ всё работает
✔ Git не ломается

Проверка:

```bash
git status
git remote -v
```

---

# 🔗 4. Привязка проекта к GitHub

```bash
git init
git add .
git commit -m "initial"
git branch -M main
git remote add origin https://github.com/USER/REPO.git
git push -u origin main
```

---

# 🔀 5. Объединение репозиториев

## Простой способ

```bash
git add .
git commit -m "merge"
git push
```

## Через remote

```bash
git remote add other https://github.com/USER/OTHER.git
git fetch other
git merge other/main
```

---

# 🔄 6. Перенос на другой компьютер

## Лучший способ

```bash
git clone https://github.com/USER/REPO.git
```

## Альтернатива

Скопировать папку вместе с `.git`

---

# 🔁 7. Ветки

```bash
git checkout -b feature/timeline
git push origin feature/timeline
```

---

# ⚠️ 8. Частые ошибки

## ❌ Забыл pull

```bash
git pull --rebase
```

---

## ❌ Коммит мусора

→ использовать `.gitignore`

---

# ❗ 9. ВАЖНО: почему .gitignore “не работает”

## Причина

```
.gitignore НЕ удаляет уже добавленные файлы
```

---

## Симптом

В репозиторий попадают:

```
bin/
obj/
```

---

## 🔥 РЕШЕНИЕ (обязательно)

Удаляем из Git, но НЕ с диска:

```bash
git rm -r --cached **/bin
git rm -r --cached **/obj
```

---

## Затем:

```bash
git commit -m "Remove build folders from repo"
git push
```

---

## ✅ Правильный .gitignore

```gitignore
**/bin/
**/obj/
.vs/
*.user
*.suo
*.log
```

---

## 🧪 Проверка

```bash
git status
```

👉 папки больше не отслеживаются

---

# 📦 10. Что нельзя коммитить

```
C:\_Portable_
bin/
obj/
*.mp4
*.log
```

---

# 🧠 11. Рекомендуемый workflow

```
git pull
→ работа
git add .
git commit
git push
```

---

# 🏁 Итог

Ты должен уметь:

✔ клонировать
✔ синхронизировать
✔ переносить проект
✔ объединять репозитории
✔ исправлять .gitignore

---

# 🚀 Следующий уровень

* CI/CD
* версии (releases)
* командная разработка

---
