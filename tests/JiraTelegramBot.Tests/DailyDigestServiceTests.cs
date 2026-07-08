using JiraTelegramBot.Configuration;
using JiraTelegramBot.Digest;
using JiraTelegramBot.Jira;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JiraTelegramBot.Tests;

public class DailyDigestServiceTests
{
    private static readonly TimeZoneInfo Dushanbe = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dushanbe");
    private static readonly DateTimeOffset Now = new(2026, 7, 7, 4, 0, 0, TimeSpan.Zero); // 09:00 Душанбе

    private static readonly JiraIssue Today = new("JIRA-1", "Сегодня", "Open", "High", null, "Task");
    private static readonly JiraIssue Overdue =
        new("JIRA-2", "Просрочка", "Open", "High", new DateOnly(2026, 7, 5), "Bug");

    private static DailyDigestService Build(
        IJiraClient jira, FakeTelegramNotifier notifier, bool sendWhenEmpty = true)
    {
        var options = Options.Create(new BotOptions
        {
            Jira = new JiraOptions { BaseUrl = "https://jira.eskhata.com" },
            Schedule = new ScheduleOptions { SendWhenEmpty = sendWhenEmpty },
        });
        return new DailyDigestService(
            jira,
            new MessageFormatter("https://jira.eskhata.com"),
            notifier,
            options,
            new FixedTimeProvider(Now),
            Dushanbe,
            NullLogger<DailyDigestService>.Instance);
    }

    private static FakeJiraClient JiraWith(IReadOnlyList<JiraIssue> today, IReadOnlyList<JiraIssue> overdue)
        => new(jql => jql.Contains("due =") ? today : overdue);

    [Fact]
    public async Task Sends_digest_with_both_sections()
    {
        var notifier = new FakeTelegramNotifier();
        var service = Build(JiraWith([Today], [Overdue]), notifier);

        await service.RunAsync(CancellationToken.None);

        var message = Assert.Single(notifier.Sent);
        Assert.Contains("На сегодня (1)", message);
        Assert.Contains("Просрочено (1)", message);
        Assert.Contains("Задачи на 07.07.2026", message);
    }

    [Fact]
    public async Task Skips_send_when_empty_and_flag_off()
    {
        var notifier = new FakeTelegramNotifier();
        var service = Build(JiraWith([], []), notifier, sendWhenEmpty: false);

        await service.RunAsync(CancellationToken.None);

        Assert.Empty(notifier.Sent);
    }

    [Fact]
    public async Task Sends_no_tasks_message_when_empty_and_flag_on()
    {
        var notifier = new FakeTelegramNotifier();
        var service = Build(JiraWith([], []), notifier, sendWhenEmpty: true);

        await service.RunAsync(CancellationToken.None);

        var message = Assert.Single(notifier.Sent);
        Assert.Contains("Нет задач", message);
    }

    [Fact]
    public async Task Sends_failure_message_when_jira_throws()
    {
        var notifier = new FakeTelegramNotifier();
        var service = Build(new FakeJiraClient(_ => throw new InvalidOperationException("boom")), notifier);

        await service.RunAsync(CancellationToken.None);

        var message = Assert.Single(notifier.Sent);
        Assert.Contains("Не смог получить задачи", message);
        Assert.Contains("boom", message);
    }

    [Fact]
    public async Task Swallows_failure_when_even_error_notify_fails()
    {
        var notifier = new FakeTelegramNotifier { ThrowOnSend = true };
        var service = Build(new FakeJiraClient(_ => throw new InvalidOperationException("boom")), notifier);

        // Не должно бросить наружу — иначе Worker упадёт.
        await service.RunAsync(CancellationToken.None);

        Assert.Empty(notifier.Sent);
    }
}
