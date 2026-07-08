# Jira → Telegram: утренние уведомления о задачах

.NET 10 Worker Service, который каждый день в **09:00 (Asia/Dushanbe)** присылает
в Telegram сводку задач из **Jira Data Center** (Scrum-процесс со спринтами):

- 📅 **В текущем спринте** — незакрытые задачи из активного спринта (`sprint in openSprints()`);
- ⚠️ **Просрочено** — незакрытые задачи из уже закрытых спринтов, не перенесённые
  в активный (`sprint in closedSprints() AND sprint not in openSprints()`).

> Поле `Due Date` в этой Jira не заполняется, поэтому отбор построен на спринтах,
> а не на сроке исполнения.

Только исходящие уведомления: ни команд, ни кнопок. Дизайн — в
[docs/superpowers/specs/2026-07-07-jira-telegram-bot-design.md](docs/superpowers/specs/2026-07-07-jira-telegram-bot-design.md).

## Структура

```
src/JiraTelegramBot/
  Program.cs                 Host + DI + валидация конфига
  Worker.cs                  фоновый цикл ожидания 09:00
  Configuration/BotOptions   типизированный конфиг
  Jira/                      JiraClient (POST /rest/api/2/search, Bearer PAT)
  Digest/                    DailyDigestService + MessageFormatter
  Telegram/                  TelegramNotifier (Telegram.Bot)
  Scheduling/                NextRunCalculator (чистый расчёт следующего запуска)
tests/JiraTelegramBot.Tests/ 25 unit-тестов (xUnit)
```

## Конфигурация

Не-секретные параметры — в `src/JiraTelegramBot/appsettings.json`
(`Bot:Jira:BaseUrl`, `Bot:Schedule:TimeZone`, `NotifyTime`, `SendWhenEmpty`).

**Секреты — только через переменные окружения** (не коммитятся):

| Переменная | Что это |
|---|---|
| `Bot__Jira__Pat` | Personal Access Token Jira DC (Profile → Personal Access Tokens) |
| `Bot__Telegram__BotToken` | Токен бота от @BotFather |
| `Bot__Telegram__ChatId` | Твой chat_id (узнать через @userinfobot) |

При отсутствии любого секрета приложение **не стартует** (fail-fast с понятной ошибкой).

## Запуск

```powershell
$env:Bot__Jira__Pat = "<jira-pat>"
$env:Bot__Telegram__BotToken = "<bot-token>"
$env:Bot__Telegram__ChatId = "<chat-id>"

dotnet run --project src/JiraTelegramBot
```

Для локальной разработки секреты удобнее хранить через `dotnet user-secrets`.

## Тесты

```powershell
dotnet test JiraTelegramBot.slnx
```

## Замечания по Jira

Проект — под **Jira Data Center** (REST API v2, Bearer PAT, классический
`/rest/api/2/search` с `startAt`). Приложенный референс описывает Jira **Cloud**
(API v3, `/search/jql`, ADF) — из него взяты только общие грабли: защита
пагинации (лимит итераций + дедуп по `key`) и обработка `429 Retry-After`.
