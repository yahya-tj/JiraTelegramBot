using System.Globalization;
using System.Net;
using System.Text;
using JiraTelegramBot.Jira;

namespace JiraTelegramBot.Digest;

/// <summary>
/// Чистое форматирование сводки в HTML для Telegram. Без I/O — легко тестировать.
/// </summary>
public sealed class MessageFormatter
{
    private readonly string _jiraBaseUrl;

    public MessageFormatter(string jiraBaseUrl)
    {
        _jiraBaseUrl = jiraBaseUrl.TrimEnd('/');
    }

    public string Format(
        IReadOnlyList<JiraIssue> today,
        IReadOnlyList<JiraIssue> overdue,
        DateOnly date)
    {
        if (today.Count == 0 && overdue.Count == 0)
            return "✅ Нет задач на сегодня и просрочек.";

        var sb = new StringBuilder();
        sb.Append("🌅 <b>Доброе утро! Задачи на ")
          .Append(date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture))
          .Append("</b>");

        if (today.Count > 0)
        {
            sb.Append("\n\n📅 <b>На сегодня (").Append(today.Count).Append(")</b>");
            foreach (var issue in today)
                sb.Append('\n').Append(FormatLine(issue, includeDue: false));
        }

        if (overdue.Count > 0)
        {
            sb.Append("\n\n⚠️ <b>Просрочено (").Append(overdue.Count).Append(")</b>");
            foreach (var issue in overdue)
                sb.Append('\n').Append(FormatLine(issue, includeDue: true));
        }

        return sb.ToString();
    }

    private string FormatLine(JiraIssue issue, bool includeDue)
    {
        var sb = new StringBuilder();
        sb.Append(" • ").Append(Link(issue.Key))
          .Append(" — ").Append(Escape(issue.Summary));

        if (includeDue && issue.DueDate is { } due)
            sb.Append(" — срок ").Append(due.ToString("dd.MM", CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(issue.Priority))
            sb.Append(" — ").Append(PriorityIcon(issue.Priority)).Append(' ').Append(Escape(issue.Priority));

        return sb.ToString();
    }

    private string Link(string key)
    {
        var safeKey = Escape(key);
        return $"<a href=\"{_jiraBaseUrl}/browse/{safeKey}\">{safeKey}</a>";
    }

    private static string PriorityIcon(string priority) => priority.ToLowerInvariant() switch
    {
        "highest" or "high" or "critical" or "blocker" => "🔴",
        "medium" or "major" => "🟡",
        "low" or "lowest" or "minor" or "trivial" => "🟢",
        _ => "⚪",
    };

    private static string Escape(string value) => WebUtility.HtmlEncode(value);
}
