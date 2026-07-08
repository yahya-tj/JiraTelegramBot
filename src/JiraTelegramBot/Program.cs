using System.Globalization;
using System.Net.Http.Headers;
using JiraTelegramBot;
using JiraTelegramBot.Configuration;
using JiraTelegramBot.Digest;
using JiraTelegramBot.Jira;
using JiraTelegramBot.Scheduling;
using JiraTelegramBot.Telegram;
using Microsoft.Extensions.Options;
using Telegram.Bot;

var builder = Host.CreateApplicationBuilder(args);

// --- Конфигурация ---
builder.Services
    .AddOptions<BotOptions>()
    .Bind(builder.Configuration.GetSection(BotOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Jira.BaseUrl), "Bot:Jira:BaseUrl не задан.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Jira.Pat), "Секрет Bot__Jira__Pat не задан.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Telegram.BotToken), "Секрет Bot__Telegram__BotToken не задан.")
    .Validate(o => o.Telegram.ChatId != 0, "Секрет Bot__Telegram__ChatId не задан.")
    .ValidateOnStart();

// --- Время и таймзона ---
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton(sp =>
{
    var schedule = sp.GetRequiredService<IOptions<BotOptions>>().Value.Schedule;
    try
    {
        return TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone);
    }
    catch (TimeZoneNotFoundException ex)
    {
        throw new InvalidOperationException(
            $"Таймзона '{schedule.TimeZone}' не найдена. Укажите корректный IANA-идентификатор " +
            "(напр. Asia/Dushanbe) в Bot:Schedule:TimeZone.", ex);
    }
});

builder.Services.AddSingleton(sp =>
{
    var schedule = sp.GetRequiredService<IOptions<BotOptions>>().Value.Schedule;
    var timeZone = sp.GetRequiredService<TimeZoneInfo>();
    if (!TimeOnly.TryParseExact(schedule.NotifyTime, "HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var notifyTime))
    {
        throw new InvalidOperationException(
            $"Bot:Schedule:NotifyTime '{schedule.NotifyTime}' некорректно. Ожидается формат HH:mm.");
    }
    return new NextRunCalculator(timeZone, notifyTime);
});

// --- Jira (типизированный HttpClient с Bearer PAT) ---
builder.Services.AddHttpClient<IJiraClient, JiraClient>((sp, http) =>
{
    var jira = sp.GetRequiredService<IOptions<BotOptions>>().Value.Jira;
    http.BaseAddress = new Uri(jira.BaseUrl.TrimEnd('/') + "/");
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jira.Pat);
    http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});

// --- Telegram ---
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var telegram = sp.GetRequiredService<IOptions<BotOptions>>().Value.Telegram;
    return new TelegramBotClient(telegram.BotToken);
});
builder.Services.AddScoped<ITelegramNotifier, TelegramNotifier>();

// --- Форматирование и оркестрация ---
builder.Services.AddSingleton(sp =>
{
    var jira = sp.GetRequiredService<IOptions<BotOptions>>().Value.Jira;
    return new MessageFormatter(jira.BaseUrl);
});
builder.Services.AddScoped<DailyDigestService>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
