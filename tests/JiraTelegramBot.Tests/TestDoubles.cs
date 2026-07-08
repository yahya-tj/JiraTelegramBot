using System.Net;
using JiraTelegramBot.Jira;
using JiraTelegramBot.Telegram;

namespace JiraTelegramBot.Tests;

/// <summary>TimeProvider с фиксированным «сейчас» для детерминированных тестов.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>Перехватывает исходящие HTTP-запросы и отдаёт заранее заданные ответы.</summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses;

    public StubHttpMessageHandler(params HttpResponseMessage[] responses)
        => _responses = new Queue<HttpResponseMessage>(responses);

    public List<string> RequestBodies { get; } = [];
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (request.Content is not null)
            RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));

        return _responses.Count > 0
            ? _responses.Dequeue()
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"total\":0,\"issues\":[]}") };
    }
}

/// <summary>Фейковый нотифаер: копит отправленные сообщения, опционально бросает исключение.</summary>
public sealed class FakeTelegramNotifier : ITelegramNotifier
{
    public List<string> Sent { get; } = [];
    public bool ThrowOnSend { get; set; }

    public Task SendAsync(string htmlText, CancellationToken ct)
    {
        if (ThrowOnSend)
            throw new InvalidOperationException("telegram down");
        Sent.Add(htmlText);
        return Task.CompletedTask;
    }
}

/// <summary>Фейковый Jira-клиент: отдаёт заданные результаты по JQL или бросает исключение.</summary>
public sealed class FakeJiraClient : IJiraClient
{
    private readonly Func<string, IReadOnlyList<JiraIssue>> _resolver;

    public FakeJiraClient(Func<string, IReadOnlyList<JiraIssue>> resolver) => _resolver = resolver;

    public Task<IReadOnlyList<JiraIssue>> SearchAsync(string jql, CancellationToken ct)
        => Task.FromResult(_resolver(jql));
}
