using JiraTelegramBot.Digest;
using JiraTelegramBot.Jira;

namespace JiraTelegramBot.Tests;

public class MessageFormatterTests
{
    private static readonly DateOnly Date = new(2026, 7, 7);
    private readonly MessageFormatter _formatter = new("https://jira.eskhata.com/");

    private static JiraIssue Issue(
        string key, string summary, string? priority = "High", DateOnly? due = null)
        => new(key, summary, "In Progress", priority, due, "Task");

    [Fact]
    public void Empty_returns_no_tasks_message()
    {
        var result = _formatter.Format([], [], Date);

        Assert.Equal("✅ Нет задач на сегодня и просрочек.", result);
    }

    [Fact]
    public void Renders_both_sections_with_counts()
    {
        var today = new[] { Issue("JIRA-1", "Задача сегодня") };
        var overdue = new[] { Issue("JIRA-2", "Просрочка", "High", new DateOnly(2026, 7, 5)) };

        var result = _formatter.Format(today, overdue, Date);

        Assert.Contains("Задачи на 07.07.2026", result);
        Assert.Contains("📅 <b>На сегодня (1)</b>", result);
        Assert.Contains("⚠️ <b>Просрочено (1)</b>", result);
        Assert.Contains("срок 05.07", result);
    }

    [Fact]
    public void Only_today_omits_overdue_section()
    {
        var result = _formatter.Format([Issue("JIRA-1", "X")], [], Date);

        Assert.Contains("На сегодня (1)", result);
        Assert.DoesNotContain("Просрочено", result);
    }

    [Fact]
    public void Only_overdue_omits_today_section()
    {
        var result = _formatter.Format([], [Issue("JIRA-9", "Y")], Date);

        Assert.Contains("Просрочено (1)", result);
        Assert.DoesNotContain("На сегодня", result);
    }

    [Fact]
    public void Builds_browse_link_from_base_url()
    {
        var result = _formatter.Format([Issue("JIRA-42", "X")], [], Date);

        Assert.Contains("<a href=\"https://jira.eskhata.com/browse/JIRA-42\">JIRA-42</a>", result);
    }

    [Fact]
    public void Escapes_html_special_chars_in_summary()
    {
        var result = _formatter.Format([Issue("JIRA-1", "Fix <script> & \"quotes\"")], [], Date);

        Assert.Contains("Fix &lt;script&gt; &amp; &quot;quotes&quot;", result);
        Assert.DoesNotContain("<script>", result);
    }

    [Theory]
    [InlineData("High", "🔴")]
    [InlineData("Medium", "🟡")]
    [InlineData("Low", "🟢")]
    [InlineData("Weird", "⚪")]
    public void Maps_priority_to_icon(string priority, string icon)
    {
        var result = _formatter.Format([Issue("JIRA-1", "X", priority)], [], Date);

        Assert.Contains(icon, result);
    }
}
