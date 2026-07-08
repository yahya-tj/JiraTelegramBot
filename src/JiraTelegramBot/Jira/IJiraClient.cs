namespace JiraTelegramBot.Jira;

public interface IJiraClient
{
    /// <summary>
    /// Выполняет JQL-поиск и возвращает все найденные задачи (с пагинацией).
    /// </summary>
    Task<IReadOnlyList<JiraIssue>> SearchAsync(string jql, CancellationToken ct);
}
