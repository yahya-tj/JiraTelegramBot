using JiraTelegramBot.Configuration;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace JiraTelegramBot.Telegram;

public sealed class TelegramNotifier : ITelegramNotifier
{
    private readonly ITelegramBotClient _bot;
    private readonly long _chatId;

    public TelegramNotifier(ITelegramBotClient bot, IOptions<BotOptions> options)
    {
        _bot = bot;
        _chatId = options.Value.Telegram.ChatId;
    }

    public async Task SendAsync(string htmlText, CancellationToken ct)
    {
        await _bot.SendMessage(
            chatId: _chatId,
            text: htmlText,
            parseMode: ParseMode.Html,
            linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
            cancellationToken: ct);
    }
}
