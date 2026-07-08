namespace JiraTelegramBot.Scheduling;

/// <summary>
/// Чистый калькулятор момента следующего запуска (без таймеров) — для 09:00
/// в заданной таймзоне. Вынесено отдельно, чтобы покрыть тестами.
/// </summary>
public sealed class NextRunCalculator
{
    private readonly TimeZoneInfo _timeZone;
    private readonly TimeOnly _notifyTime;

    public NextRunCalculator(TimeZoneInfo timeZone, TimeOnly notifyTime)
    {
        _timeZone = timeZone;
        _notifyTime = notifyTime;
    }

    /// <summary>
    /// Возвращает ближайший момент времени в будущем, соответствующий
    /// заданному времени уведомления. Если сейчас ровно это время — берём завтра.
    /// </summary>
    public DateTimeOffset GetNextRun(DateTimeOffset now)
    {
        var nowInZone = TimeZoneInfo.ConvertTime(now, _timeZone);
        var candidate = nowInZone.Date.Add(_notifyTime.ToTimeSpan());

        if (candidate <= nowInZone.DateTime)
            candidate = candidate.AddDays(1);

        var offset = _timeZone.GetUtcOffset(candidate);
        return new DateTimeOffset(candidate, offset);
    }
}
