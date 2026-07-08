namespace JiraTelegramBot.Configuration;

/// <summary>
/// Корневой конфиг бота. Не-секретные поля берутся из appsettings.json,
/// секреты (PAT, токен, chatId) — из переменных окружения.
/// </summary>
public sealed class BotOptions
{
    public const string SectionName = "Bot";

    public JiraOptions Jira { get; set; } = new();
    public TelegramOptions Telegram { get; set; } = new();
    public ScheduleOptions Schedule { get; set; } = new();
}

public sealed class JiraOptions
{
    /// <summary>Базовый URL Jira Data Center, напр. https://jira.eskhata.com</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Personal Access Token (Bearer). Только из переменной окружения.</summary>
    public string Pat { get; set; } = string.Empty;

    /// <summary>Размер страницы при пагинации поиска.</summary>
    public int PageSize { get; set; } = 50;

    /// <summary>Жёсткий лимит итераций пагинации (защита от бесконечного цикла).</summary>
    public int MaxPages { get; set; } = 50;
}

public sealed class TelegramOptions
{
    /// <summary>Токен бота. Только из переменной окружения.</summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>ID чата получателя. Только из переменной окружения.</summary>
    public long ChatId { get; set; }
}

public sealed class ScheduleOptions
{
    /// <summary>IANA-идентификатор таймзоны, напр. Asia/Dushanbe.</summary>
    public string TimeZone { get; set; } = "Asia/Dushanbe";

    /// <summary>Время ежедневного уведомления в формате HH:mm.</summary>
    public string NotifyTime { get; set; } = "09:00";

    /// <summary>Слать ли сообщение, когда задач нет.</summary>
    public bool SendWhenEmpty { get; set; } = true;
}
