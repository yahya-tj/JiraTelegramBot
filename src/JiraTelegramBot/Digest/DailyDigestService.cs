using JiraTelegramBot.Configuration;
using JiraTelegramBot.Jira;
using JiraTelegramBot.Telegram;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JiraTelegramBot.Digest;

/// <summary>
/// Оркестратор дневной сводки: собирает задачи из Jira, форматирует и шлёт в Telegram.
/// </summary>
public sealed class DailyDigestService
{
    private const string TodayJql =
        "assignee = currentUser() AND statusCategory != Done " +
        "AND due = startOfDay() ORDER BY priority DESC";

    private const string OverdueJql =
        "assignee = currentUser() AND statusCategory != Done " +
        "AND due < startOfDay() ORDER BY due ASC";

    private readonly IJiraClient _jira;
    private readonly MessageFormatter _formatter;
    private readonly ITelegramNotifier _notifier;
    private readonly ScheduleOptions _schedule;
    private readonly TimeProvider _clock;
    private readonly TimeZoneInfo _timeZone;
    private readonly ILogger<DailyDigestService> _logger;

    public DailyDigestService(
        IJiraClient jira,
        MessageFormatter formatter,
        ITelegramNotifier notifier,
        IOptions<BotOptions> options,
        TimeProvider clock,
        TimeZoneInfo timeZone,
        ILogger<DailyDigestService> logger)
    {
        _jira = jira;
        _formatter = formatter;
        _notifier = notifier;
        _schedule = options.Value.Schedule;
        _clock = clock;
        _timeZone = timeZone;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            var today = await _jira.SearchAsync(TodayJql, ct);
            var overdue = await _jira.SearchAsync(OverdueJql, ct);

            _logger.LogInformation(
                "Собрана сводка: сегодня {Today}, просрочено {Overdue}.",
                today.Count, overdue.Count);

            if (today.Count == 0 && overdue.Count == 0 && !_schedule.SendWhenEmpty)
            {
                _logger.LogInformation("Задач нет и SendWhenEmpty=false — сообщение не отправляется.");
                return;
            }

            var localDate = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _timeZone).DateTime);

            var message = _formatter.Format(today, overdue, localDate);
            await _notifier.SendAsync(message, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось сформировать/отправить дневную сводку.");
            await TryNotifyFailureAsync(ex, ct);
        }
    }

    private async Task TryNotifyFailureAsync(Exception ex, CancellationToken ct)
    {
        try
        {
            await _notifier.SendAsync(
                $"⚠️ Не смог получить задачи из Jira: {System.Net.WebUtility.HtmlEncode(ex.Message)}",
                ct);
        }
        catch (Exception notifyEx)
        {
            _logger.LogError(notifyEx, "Не удалось отправить даже сообщение об ошибке в Telegram.");
        }
    }
}
