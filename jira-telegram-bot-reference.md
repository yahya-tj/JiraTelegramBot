# Референс: интеграция Jira Cloud с Telegram-ботом (2026)

Практический справочник для разработчика: авторизация, чтение задач, смена
статуса, назначение, комментарии и типовые грабли. Актуально на 2026 год с
учётом удаления старого search-эндпоинта Jira Cloud.

---

## 1. Авторизация

Для Telegram-бота два пути. Выбор зависит от того, кто «действует» в Jira.

### Basic Auth с API-токеном (рекомендуется для бота)

Проще всего: бот работает от лица одного сервисного аккаунта. Jira Cloud
предоставляет REST API v3 по адресу `https://your-domain.atlassian.net/rest/api/3`
и поддерживает OAuth 2.0 (3LO) и API Token (Basic Auth). Токен создаётся в
id.atlassian.com → Security → API tokens. Аутентификация — заголовок с Base64 от
`email:token`.

```python
import base64, requests

EMAIL = "bot-service@company.com"
TOKEN = "xxxxx"
BASE = "https://your-domain.atlassian.net/rest/api/3"

auth = base64.b64encode(f"{EMAIL}:{TOKEN}".encode()).decode()
H = {
    "Authorization": f"Basic {auth}",
    "Accept": "application/json",
    "Content-Type": "application/json",
}
```

**Грабли Basic Auth:**

- API-токены истекают ровно через год, без программного пути обновления и без
  предупреждения об истечении через API. Деактивация аккаунта-владельца
  немедленно отзывает токен без grace-периода. Поставьте календарное напоминание
  на ротацию.
- Бот наследует **все** права сервисного аккаунта: API-токены не поддерживают
  гранулярные скоупы. Дайте аккаунту минимально нужные права на нужные проекты.

### OAuth 2.0 (3LO) — если действия должны идти от лица конкретного пользователя

Тогда каждый пользователь бота проходит «OAuth dance», а бот хранит их
refresh-токены. Регистрируете приложение на developer.atlassian.com/console,
выбираете OAuth 2.0 (3LO), настраиваете redirect URI и запрашиваете нужные
скоупы. Меняете authorization code на access token через
`POST https://auth.atlassian.com/oauth/token`. Base URL при этом другой:
`https://api.atlassian.com/ex/jira/<cloudId>/rest/api/3/...`.

**Нюансы OAuth:**

- Чтобы получить refresh-токен, нужно добавить `offline_access` в scope.
  Refresh-токены ротируемые — при каждом использовании выдаётся новый
  refresh-токен с ограниченным сроком жизни. Значит, вы **обязаны** сохранять
  новый refresh-токен после каждого обновления, иначе следующее обновление
  отвалится.
- Разрешения пользователя всегда ограничивают приложение — независимо от
  скоупов приложения.

> Для банковского контекста: OAuth 3LO даёт аудит «кто что сделал», но усложняет
> архитектуру хранением токенов. Basic Auth от сервисного аккаунта проще, но
> требует своего слоя маппинга Telegram-user → права.

---

## 2. Чтение задач (главная ловушка 2025–2026)

Старый search endpoint **удалён**. Это самое частое, на чём ломаются интеграции.

Легаси-эндпоинт `/rest/api/3/search` deprecated и полностью удалён из Jira Cloud;
все обращения нужно перевести на `/rest/api/3/search/jql`. Затронуты и v2, и v3
варианты (`/rest/api/2/search`, `/rest/api/latest/search`).

**Ключевые отличия нового эндпоинта:**

- Пагинация теперь по токену `nextPageToken` вместо устаревшего `startAt`.
- Поле `total` больше не возвращается. Для счётчика — отдельный вызов
  `POST /rest/api/3/search/approximate-count`.
- **Критично:** поля по умолчанию изменились. В `/search/jql` дефолт для
  `fields` — только `id` (у старого `/search` было `*navigable`). Если не указать
  `fields` явно — получите пустые задачи и решите, что API «не работает» (частая
  жалоба на форумах).

```python
def search_issues(jql, fields=None, page_size=50):
    fields = fields or ["key", "summary", "status", "assignee", "priority"]
    results, token = [], None
    while True:
        body = {"jql": jql, "maxResults": page_size, "fields": fields}
        if token:
            body["nextPageToken"] = token
        r = requests.post(f"{BASE}/search/jql", headers=H, json=body)
        r.raise_for_status()
        data = r.json()
        results.extend(data.get("issues", []))
        token = data.get("nextPageToken")
        if data.get("isLast") or not token:
            break
    return results

# Мои открытые задачи для карточки в Telegram:
issues = search_issues(
    'assignee = currentUser() AND statusCategory != Done ORDER BY updated DESC'
)
```

Одиночная задача читается по-старому и надёжно: `GET /rest/api/3/issue/{key}`.

**Грабли поиска:**

- Есть известные жалобы на сломанную пагинацию: `isLast` не возвращает `true`, а
  `nextPageToken` бесконечно выдаёт новые токены, всегда загружая первую
  страницу. Защититесь лимитом на число итераций и дедупликацией по `key`, чтобы
  бот не зациклился.
- JQL-запросы больше не валидируются автоматически, ошибки нужно обрабатывать
  вручную.
- Для GET-версии JQL надо URL-кодировать; для сложных запросов используйте POST —
  тогда кодировать не нужно.
- `accountId` — единственный поддерживаемый идентификатор пользователя после
  GDPR-изменений 2019 года; `username` и `userKey` полностью удалены.

Если используете Python-библиотеку `jira`: старые версии дергают удалённый
эндпоинт и падают с ошибкой депрекейшена — нужен метод `enhanced_search_issues`
вместо `search`. Обновите пакет до свежей версии.

---

## 3. Смена статуса (через transitions, не прямой записью)

Статус нельзя просто «поставить» — только через переход (transition), и
доступные переходы зависят от текущего состояния и workflow.

```python
def get_transitions(key):
    r = requests.get(f"{BASE}/issue/{key}/transitions", headers=H)
    return r.json()["transitions"]  # [{id, name, to:{name}}, ...]

def transition_issue(key, transition_id, comment=None):
    body = {"transition": {"id": transition_id}}
    if comment:
        body["update"] = {"comment": [{"add": {"body": adf(comment)}}]}
    r = requests.post(f"{BASE}/issue/{key}/transitions", headers=H, json=body)
    r.raise_for_status()  # 204 при успехе
```

**Грабли статусов:**

- ID перехода ≠ ID статуса. Всегда сначала запрашивайте `/transitions` — нельзя
  хардкодить ID перехода «In Progress», он различается между workflow и
  проектами.
- Если нужного перехода нет в списке — значит, из текущего статуса он недоступен
  по workflow (не баг). В боте показывайте только реально доступные кнопки,
  подтянув их из ответа.
- Переход может требовать обязательных полей на экране перехода (resolution,
  комментарий) — тогда вернётся 400 со списком полей. Их надо передать в `fields`.

---

## 4. Назначение исполнителя

```python
def assign_issue(key, account_id):
    body = {"accountId": account_id}  # None = снять, -1 = default assignee
    r = requests.put(f"{BASE}/issue/{key}/assignee", headers=H, json=body)
    r.raise_for_status()  # 204
```

**Грабли назначения:**

- Только `accountId`. Поиск accountId по имени/почте —
  `GET /rest/api/3/user/search?query=...`.
- Поле email может быть скрыто из ответов API настройками видимости профиля даже
  при использовании admin-токена, что делает поиск по email ненадёжным.
  accountId — единственный стабильный идентификатор в v3. Кэшируйте маппинг
  Telegram-user → accountId у себя, не полагайтесь на резолв по email в рантайме.

---

## 5. Комментарии (ADF — вторая большая ловушка)

API v3 использует **Atlassian Document Format** (структурированный JSON), а не
plain text и не markdown. Это ломает новичков, привыкших слать строку.

```python
def adf(text):
    return {
        "type": "doc",
        "version": 1,
        "content": [{
            "type": "paragraph",
            "content": [{"type": "text", "text": text}]
        }]
    }

def add_comment(key, text):
    r = requests.post(f"{BASE}/issue/{key}/comment",
                      headers=H, json={"body": adf(text)})
    r.raise_for_status()  # 201
```

**Грабли комментариев:**

- В v3 нельзя послать `{"body": "просто строка"}` — получите 400. Нужен
  ADF-объект.
- Обходной путь, если ADF мешает: использовать API v2
  (`/rest/api/2/issue/{key}/comment`), который принимает plain text/wiki-разметку.
  v2 пока живёт, но стратегически лучше остаться на v3 + ADF.
- Форматирование из Telegram (markdown) в ADF придётся конвертировать вручную
  или библиотекой — прямого маппинга нет.
- Чтение комментариев: `GET /rest/api/3/issue/{key}/comment`, тело придёт как
  ADF — для показа в Telegram надо извлечь текст из `content`.

---

## 6. Общие грабли и рекомендации под банковский контекст

**Пагинация различается между API.** JSM Service Desk API использует start/limit,
Jira platform API — startAt/maxResults, а поиск теперь — nextPageToken.
Смешивание в одном пайплайне даёт тихое усечение результатов.

**Вебхуки не покрывают всё.** Нативные вебхуки Jira не эмитят события жизненного
цикла пользователя (создание, деактивация) — их нужно наблюдать через Atlassian
Guard audit log API или SCIM. Для бота: события по задачам
(created/updated/commented) через webhook получите нормально. Для реактивного
Telegram-бота настройте либо webhook (Jira → ваш HTTPS endpoint), либо polling
через `/search/jql` по `updated >= -Nm`.

**Rate limiting.** Jira Cloud лимитирует по cost-based модели; при 429 читайте
`Retry-After`. Для бота с массой пользователей делайте очередь и backoff.

**Для банковского продукта:**

- Сервисный аккаунт бота — с минимальными правами на конкретные проекты, ротация
  токена по календарю.
- Все side-effect действия (смена статуса, назначение, комментарии — особенно
  если бот пишет от общего аккаунта) логируйте на своей стороне с привязкой к
  Telegram-user, потому что в Jira это будет один автор.
- Секреты (токен, client_secret) — в vault, не в коде и не в переменных Docker
  Compose открытым текстом.

---

## Шпаргалка по эндпоинтам

| Действие | Метод и путь |
|---|---|
| Поиск задач | `POST /rest/api/3/search/jql` |
| Счётчик задач | `POST /rest/api/3/search/approximate-count` |
| Одна задача | `GET /rest/api/3/issue/{key}` |
| Доступные переходы | `GET /rest/api/3/issue/{key}/transitions` |
| Смена статуса | `POST /rest/api/3/issue/{key}/transitions` |
| Назначение | `PUT /rest/api/3/issue/{key}/assignee` |
| Добавить комментарий | `POST /rest/api/3/issue/{key}/comment` |
| Читать комментарии | `GET /rest/api/3/issue/{key}/comment` |
| Поиск пользователя | `GET /rest/api/3/user/search?query=...` |
| OAuth token exchange | `POST https://auth.atlassian.com/oauth/token` |
