namespace JiraTelegramBot.Telegram;

public interface ITelegramNotifier
{
    /// <summary>Отправляет HTML-сообщение в настроенный чат.</summary>
    Task SendAsync(string htmlText, CancellationToken ct);
}
