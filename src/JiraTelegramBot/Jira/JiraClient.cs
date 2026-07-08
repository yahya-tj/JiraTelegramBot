using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JiraTelegramBot.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JiraTelegramBot.Jira;

/// <summary>
/// Клиент Jira Data Center. Ходит на классический POST /rest/api/2/search
/// (в DC он штатный и живой, в отличие от удалённого /search в Cloud).
/// Авторизация — Bearer PAT (настраивается в DI на HttpClient).
/// </summary>
public sealed class JiraClient : IJiraClient
{
    // Поля запрашиваем явно, чтобы не зависеть от дефолтов сервера.
    private static readonly string[] RequestedFields =
        ["summary", "status", "priority", "duedate", "issuetype"];

    private const int MaxRetriesOn429 = 3;

    private readonly HttpClient _http;
    private readonly JiraOptions _options;
    private readonly ILogger<JiraClient> _logger;

    public JiraClient(HttpClient http, IOptions<BotOptions> options, ILogger<JiraClient> logger)
    {
        _http = http;
        _options = options.Value.Jira;
        _logger = logger;
    }

    public async Task<IReadOnlyList<JiraIssue>> SearchAsync(string jql, CancellationToken ct)
    {
        var collected = new List<JiraIssue>();
        // Дедуп по key — защита от известного бага пагинации (первая страница
        // выдаётся бесконечно). Плюс жёсткий лимит итераций ниже.
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        var startAt = 0;
        for (var page = 0; page < _options.MaxPages; page++)
        {
            var response = await SearchPageAsync(jql, startAt, ct);

            var newInThisPage = 0;
            foreach (var raw in response.Issues)
            {
                var issue = Map(raw);
                if (seenKeys.Add(issue.Key))
                {
                    collected.Add(issue);
                    newInThisPage++;
                }
            }

            var fetched = response.Issues.Count;
            startAt += fetched;

            // Условия остановки: пустая страница, дошли до total, либо страница
            // не принесла ничего нового (зациклившаяся пагинация).
            if (fetched == 0 || startAt >= response.Total || newInThisPage == 0)
                break;

            if (page == _options.MaxPages - 1)
                _logger.LogWarning(
                    "Достигнут лимит страниц ({MaxPages}) для JQL: {Jql}. Возможно, усечён результат.",
                    _options.MaxPages, jql);
        }

        return collected;
    }

    private async Task<SearchResponse> SearchPageAsync(string jql, int startAt, CancellationToken ct)
    {
        var body = new SearchRequest
        {
            Jql = jql,
            StartAt = startAt,
            MaxResults = _options.PageSize,
            Fields = RequestedFields,
        };

        for (var attempt = 0; ; attempt++)
        {
            using var response = await _http.PostAsJsonAsync("rest/api/2/search", body, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetriesOn429)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
                _logger.LogWarning("Jira вернула 429, повтор через {Delay}s.", delay.TotalSeconds);
                await Task.Delay(delay, ct);
                continue;
            }

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SearchResponse>(ct);
            return result ?? new SearchResponse();
        }
    }

    private static JiraIssue Map(RawIssue raw)
    {
        var f = raw.Fields;
        DateOnly? due = null;
        if (!string.IsNullOrWhiteSpace(f?.DueDate) &&
            DateOnly.TryParse(f.DueDate, out var parsed))
        {
            due = parsed;
        }

        return new JiraIssue(
            Key: raw.Key,
            Summary: f?.Summary ?? string.Empty,
            Status: f?.Status?.Name ?? "Unknown",
            Priority: f?.Priority?.Name,
            DueDate: due,
            IssueType: f?.IssueType?.Name);
    }

    // --- DTO для сериализации запроса/ответа ---

    private sealed class SearchRequest
    {
        [JsonPropertyName("jql")] public string Jql { get; init; } = string.Empty;
        [JsonPropertyName("startAt")] public int StartAt { get; init; }
        [JsonPropertyName("maxResults")] public int MaxResults { get; init; }
        [JsonPropertyName("fields")] public string[] Fields { get; init; } = [];
    }

    private sealed class SearchResponse
    {
        [JsonPropertyName("total")] public int Total { get; init; }
        [JsonPropertyName("issues")] public List<RawIssue> Issues { get; init; } = [];
    }

    private sealed class RawIssue
    {
        [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;
        [JsonPropertyName("fields")] public RawFields? Fields { get; init; }
    }

    private sealed class RawFields
    {
        [JsonPropertyName("summary")] public string? Summary { get; init; }
        [JsonPropertyName("duedate")] public string? DueDate { get; init; }
        [JsonPropertyName("status")] public NamedField? Status { get; init; }
        [JsonPropertyName("priority")] public NamedField? Priority { get; init; }
        [JsonPropertyName("issuetype")] public NamedField? IssueType { get; init; }
    }

    private sealed class NamedField
    {
        [JsonPropertyName("name")] public string? Name { get; init; }
    }
}
