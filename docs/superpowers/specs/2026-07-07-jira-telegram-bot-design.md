# Дизайн: Telegram-бот утренних уведомлений о задачах Jira

**Дата:** 2026-07-07
**Статус:** утверждён к реализации

## Цель

.NET Worker Service, который каждый день в **09:00 по Asia/Dushanbe** присылает
одному пользователю в Telegram сводку его задач из **Jira Data Center**:

- задачи со сроком **на сегодня** (due = сегодня, не закрытые);
- **просроченные** задачи (due < сегодня, не закрытые).

Только исходящие уведомления. Никаких команд, кнопок, смены статусов —
осознанный YAGNI на первую версию.

## Ключевые решения и контекст

- **Jira Data Center**, НЕ Cloud. Референс `jira-telegram-bot-reference.md`
  описывает Cloud (API v3, `/search/jql`, ADF, `accountId`). Для DC используем:
  - REST API **v2**;
  - авторизация через **Personal Access Token** (заголовок `Authorization: Bearer <PAT>`);
  - классический `POST /rest/api/2/search` с пагинацией `startAt/maxResults`;
  - идентификация через `currentUser()` (PAT принадлежит самому пользователю).
- Из референса берём общие грабли: защита пагинации (лимит итераций + дедуп по
  `key`), обработка 429 по `Retry-After`, секреты вне кода.
- **Один получатель** — персональный бот. Один `chatId`, задачи через `currentUser()`.
- **.NET 10** (LTS), Worker Service (`BackgroundService`).
- **Telegram.Bot** (NuGet) как клиент — нужен только `SendMessage`.
- Планировщик — самодельный расчёт задержки до следующих 09:00 через
  `Task.Delay` (без Quartz/Cronos — оверкилл для одного джоба в день).

## Архитектура

```
┌─────────────────────── Worker Service (24/7) ───────────────────────┐
│  Worker (BackgroundService)                                          │
│    └─ NextRunCalculator ── ждём до 09:00 Asia/Dushanbe               │
│         └─ DailyDigestService                                        │
│              ├─ JiraClient ──HTTP──▶ Jira DC /rest/api/2/search      │
│              │                        (Bearer PAT)                   │
│              ├─ MessageFormatter (issues → HTML-текст)               │
│              └─ TelegramNotifier ──HTTP──▶ Telegram Bot API          │
└──────────────────────────────────────────────────────────────────────┘
```

Цикл Worker: вычислить следующий запуск → `Task.Delay` до него → выполнить
дневной прогон в `try/catch` → повторить. Сбой одного дня не роняет сервис.

## Компоненты

Каждый компонент — одна ответственность, общается через интерфейс, тестируется
изолированно.

- **`BotOptions`** (+ вложенные `JiraOptions`, `TelegramOptions`, `ScheduleOptions`)
  — типизированный конфиг. Не-секретное из `appsettings.json`, секреты из
  переменных окружения.
- **`IJiraClient` / `JiraClient`** — единственный, кто ходит в Jira.
  `Task<IReadOnlyList<JiraIssue>> SearchAsync(string jql, CancellationToken ct)`.
  Внутри: Bearer-заголовок, `POST /rest/api/2/search`, пагинация с **жёстким
  лимитом итераций и дедупом по `key`**, обработка 429 по `Retry-After`.
- **`JiraIssue`** — плоская модель: `Key, Summary, Status, Priority, DueDate, IssueType`.
- **`DailyDigestService`** — оркестратор: строит два JQL, зовёт `JiraClient`,
  передаёт `MessageFormatter`, шлёт через `TelegramNotifier`.
- **`MessageFormatter`** — чистая функция `(today, overdue, date) → string` (HTML).
  Без I/O.
- **`ITelegramNotifier` / `TelegramNotifier`** — обёртка над `Telegram.Bot`,
  только `SendMessage(chatId, htmlText, ct)`.
- **`NextRunCalculator`** — чистая функция `(DateTimeOffset now) → DateTimeOffset`
  для 09:00 в заданной TZ. Без таймеров внутри → тестируемо.
- **`Worker`** — связывает `NextRunCalculator` + `Task.Delay` + `DailyDigestService`.

## JQL

> **Обновление (2026-07-08):** в реальной Jira банка поле `Due Date` не
> заполняется (0 задач), работа ведётся по спринтам. Отбор переведён с `due`
> на спринты. Ниже — актуальный вариант.

```
В спринте:  assignee = currentUser() AND statusCategory != Done
            AND sprint in openSprints() ORDER BY priority DESC

Просрочено: assignee = currentUser() AND statusCategory != Done
            AND sprint in closedSprints() AND sprint not in openSprints()
            ORDER BY priority DESC
```

`fields`: `["summary","status","priority","duedate","issuetype"]`. `key`
возвращается всегда. `statusCategory != Done` ловит все незакрытые статусы
независимо от кастомного workflow. «Просрочено» = незавершённые задачи из уже
закрытых спринтов, не перенесённые в активный.

## Формат сообщения (HTML)

Ссылки на `{JiraBaseUrl}/browse/{KEY}`.

```
🌅 Доброе утро! Задачи на 07.07.2026

📅 На сегодня (2)
 • JIRA-123 — Настроить бэкап — 🔴 High
 • JIRA-124 — Ревью PR — 🟡 Medium

⚠️ Просрочено (1)
 • JIRA-100 — Обновить сертификат — срок 05.07 — 🔴 High
```

**Если задач нет** — короткое «✅ Нет задач на сегодня и просрочек». Управляется
флагом `SendWhenEmpty` (по умолчанию `true`), чтобы пользователь видел, что бот жив.

Спецсимволы HTML в `summary` экранируются.

## Обработка ошибок

- **Jira/сеть недоступна:** ловим, логируем, шлём в Telegram
  «⚠️ Не смог получить задачи из Jira (причина)» — отсутствие сообщения не должно
  быть двусмысленным.
- **429:** читаем `Retry-After`, ждём, ограниченное число повторов.
- **Пагинация:** жёсткий лимит итераций + дедуп по `key`.
- **Сбой дня:** `try/catch` вокруг прогона, Worker продолжает цикл.
- **Таймзона:** `Asia/Dushanbe`; на Windows .NET сопоставит через ICU. Понятная
  ошибка при ненайденной зоне.

## Конфигурация и секреты

`appsettings.json` (не-секретное): `JiraBaseUrl`, `TimeZone`, `NotifyTime`
(`09:00`), `SendWhenEmpty`.

**Секреты — только через переменные окружения:** `JIRA__PAT`,
`TELEGRAM__BOTTOKEN`, `TELEGRAM__CHATID`. Не в коде, не в git. `.gitignore` +
user-secrets/`appsettings.Development.json` для локальной разработки.

## Тестирование (xUnit)

- **`NextRunCalculator`** — таблица: «08:00 → сегодня 09:00», «09:30 → завтра
  09:00», «ровно 09:00 → завтра», стабильность в Asia/Dushanbe.
- **`MessageFormatter`** — обе секции, только сегодня, только просрочка, пусто,
  HTML-эскейпинг спецсимволов в summary.
- **`JiraClient`** — мок `HttpMessageHandler`: правильные URL/заголовок/тело JQL,
  разбор ответа, пагинация в 2 страницы, дедуп, стоп по лимиту итераций,
  обработка 429.
- **`TelegramNotifier`** — мок отправки, вызов с нужным `chatId`.

## Структура проекта

```
HW2/
  JiraTelegramBot.sln
  src/JiraTelegramBot/
    Program.cs                 (Host + DI + Options)
    Worker.cs
    Configuration/BotOptions.cs
    Jira/{IJiraClient.cs, JiraClient.cs, JiraIssue.cs}
    Digest/{DailyDigestService.cs, MessageFormatter.cs}
    Telegram/{ITelegramNotifier.cs, TelegramNotifier.cs}
    Scheduling/NextRunCalculator.cs
    appsettings.json
  tests/JiraTelegramBot.Tests/
```

## Явно вне scope (первая версия)

- Команды/кнопки Telegram, интерактив.
- Смена статуса, назначение, комментарии.
- Несколько получателей / маппинг Telegram → Jira.
- Webhook от Jira, реактивные уведомления.
- Хранилище состояния/БД.
