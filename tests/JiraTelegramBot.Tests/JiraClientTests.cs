using System.Net;
using JiraTelegramBot.Configuration;
using JiraTelegramBot.Jira;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JiraTelegramBot.Tests;

public class JiraClientTests
{
    private static HttpResponseMessage Ok(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private static string IssueJson(string key) =>
        "{\"key\":\"" + key + "\",\"fields\":{" +
        "\"summary\":\"Summary " + key + "\"," +
        "\"status\":{\"name\":\"In Progress\"}," +
        "\"priority\":{\"name\":\"High\"}," +
        "\"duedate\":\"2026-07-07\"," +
        "\"issuetype\":{\"name\":\"Task\"}}}";

    private static string Page(int total, params string[] keys) =>
        "{\"total\":" + total + ",\"issues\":[" + string.Join(",", keys.Select(IssueJson)) + "]}";

    private static JiraClient Build(StubHttpMessageHandler handler, int pageSize = 2, int maxPages = 50)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://jira.eskhata.com/") };
        var options = Options.Create(new BotOptions
        {
            Jira = new JiraOptions { PageSize = pageSize, MaxPages = maxPages },
        });
        return new JiraClient(http, options, NullLogger<JiraClient>.Instance);
    }

    [Fact]
    public async Task Posts_to_v2_search_with_jql_and_fields()
    {
        var handler = new StubHttpMessageHandler(Ok(Page(1, "JIRA-1")));
        var client = Build(handler);

        await client.SearchAsync("assignee = currentUser()", CancellationToken.None);

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://jira.eskhata.com/rest/api/2/search", request.RequestUri!.ToString());
        var body = handler.RequestBodies.Single();
        Assert.Contains("assignee = currentUser()", body);
        Assert.Contains("summary", body);
        Assert.Contains("duedate", body);
    }

    [Fact]
    public async Task Maps_fields_into_issue()
    {
        var handler = new StubHttpMessageHandler(Ok(Page(1, "JIRA-7")));
        var client = Build(handler);

        var issues = await client.SearchAsync("x", CancellationToken.None);

        var issue = Assert.Single(issues);
        Assert.Equal("JIRA-7", issue.Key);
        Assert.Equal("Summary JIRA-7", issue.Summary);
        Assert.Equal("In Progress", issue.Status);
        Assert.Equal("High", issue.Priority);
        Assert.Equal(new DateOnly(2026, 7, 7), issue.DueDate);
        Assert.Equal("Task", issue.IssueType);
    }

    [Fact]
    public async Task Paginates_until_total_reached()
    {
        var handler = new StubHttpMessageHandler(
            Ok(Page(3, "JIRA-1", "JIRA-2")),
            Ok(Page(3, "JIRA-3")));
        var client = Build(handler, pageSize: 2);

        var issues = await client.SearchAsync("x", CancellationToken.None);

        Assert.Equal(3, issues.Count);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Stops_and_dedupes_on_looping_pagination()
    {
        // Сервер бесконечно отдаёт одну и ту же первую страницу с total=100.
        var handler = new StubHttpMessageHandler(
            Ok(Page(100, "JIRA-1", "JIRA-2")),
            Ok(Page(100, "JIRA-1", "JIRA-2")),
            Ok(Page(100, "JIRA-1", "JIRA-2")));
        var client = Build(handler, pageSize: 2);

        var issues = await client.SearchAsync("x", CancellationToken.None);

        // Дедуп по key: только 2 уникальные задачи, и мы не зациклились.
        Assert.Equal(2, issues.Count);
        Assert.True(handler.Requests.Count <= 2);
    }

    [Fact]
    public async Task Retries_on_429_then_succeeds()
    {
        var tooMany = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        tooMany.Headers.TryAddWithoutValidation("Retry-After", "0");
        var handler = new StubHttpMessageHandler(tooMany, Ok(Page(1, "JIRA-1")));
        var client = Build(handler);

        var issues = await client.SearchAsync("x", CancellationToken.None);

        Assert.Single(issues);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Respects_max_pages_cap()
    {
        // Каждая страница приносит НОВЫЕ ключи и total огромный — остановит только лимит страниц.
        var responses = Enumerable.Range(0, 10)
            .Select(i => Ok(Page(1000, $"JIRA-{i}A", $"JIRA-{i}B")))
            .ToArray();
        var handler = new StubHttpMessageHandler(responses);
        var client = Build(handler, pageSize: 2, maxPages: 3);

        var issues = await client.SearchAsync("x", CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(6, issues.Count);
    }
}
