namespace JiraTelegramBot.Jira;

/// <summary>
/// Плоская модель задачи Jira — только поля, нужные для сводки.
/// </summary>
public sealed record JiraIssue(
    string Key,
    string Summary,
    string Status,
    string? Priority,
    DateOnly? DueDate,
    string? IssueType);
