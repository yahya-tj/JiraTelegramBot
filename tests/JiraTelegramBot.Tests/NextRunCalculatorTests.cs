using JiraTelegramBot.Scheduling;

namespace JiraTelegramBot.Tests;

public class NextRunCalculatorTests
{
    // Asia/Dushanbe = UTC+5 круглый год (без DST).
    private static readonly TimeZoneInfo Dushanbe = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dushanbe");
    private static readonly TimeOnly NineAm = new(9, 0);

    private static NextRunCalculator Calc() => new(Dushanbe, NineAm);

    [Fact]
    public void Before_notify_time_returns_today_9am()
    {
        // 08:00 в Душанбе = 03:00 UTC.
        var now = new DateTimeOffset(2026, 7, 7, 3, 0, 0, TimeSpan.Zero);

        var next = Calc().GetNextRun(now);

        // Ожидаем 09:00 (+05:00) того же дня.
        Assert.Equal(new DateTimeOffset(2026, 7, 7, 9, 0, 0, TimeSpan.FromHours(5)), next);
    }

    [Fact]
    public void After_notify_time_returns_tomorrow_9am()
    {
        // 09:30 в Душанбе = 04:30 UTC.
        var now = new DateTimeOffset(2026, 7, 7, 4, 30, 0, TimeSpan.Zero);

        var next = Calc().GetNextRun(now);

        Assert.Equal(new DateTimeOffset(2026, 7, 8, 9, 0, 0, TimeSpan.FromHours(5)), next);
    }

    [Fact]
    public void Exactly_notify_time_returns_tomorrow()
    {
        // Ровно 09:00 в Душанбе = 04:00 UTC.
        var now = new DateTimeOffset(2026, 7, 7, 4, 0, 0, TimeSpan.Zero);

        var next = Calc().GetNextRun(now);

        Assert.Equal(new DateTimeOffset(2026, 7, 8, 9, 0, 0, TimeSpan.FromHours(5)), next);
    }

    [Fact]
    public void Next_run_is_always_in_the_future()
    {
        var now = new DateTimeOffset(2026, 7, 7, 4, 0, 0, TimeSpan.Zero);

        var next = Calc().GetNextRun(now);

        Assert.True(next > now);
    }
}
