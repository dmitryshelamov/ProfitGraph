using System.Net;

using Microsoft.Extensions.Options;

using ProfitGraph.Application.Notifications;
using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Infrastructure.Notifications;

namespace ProfitGraph.Tests.Unit.Infrastructure.Notifications;

/// <summary>
///     The Telegram HTTP client (ADR 0020): its paths stay relative although a token has a colon, a 403 send is a blocked
///     chat, and no failure carries the token. The token is invented.
/// </summary>
public sealed class TelegramHttpClientTests
{
    private const string Token = "123456:test-token-not-real";

    [Test]
    public async Task UpdatesAreReadFromTheTokensPath()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            body: """{"ok":true,"result":[{"update_id":7,"message":{"chat":{"id":42},"text":"/start ABC"}},{"update_id":8,"edited_message":{}}]}""");

        IReadOnlyList<TelegramUpdateInternal> updates = await Client(handler).GetUpdates(offset: 7, TimeSpan.FromSeconds(1), CancellationToken.None);

        handler.Path.ShouldBe($"/bot{Token}/getUpdates");
        updates.ShouldBe([new TelegramUpdateInternal(UpdateId: 7, ChatId: 42, "/start ABC"), new TelegramUpdateInternal(UpdateId: 8, ChatId: null, Text: null)]);
    }

    [Test]
    public async Task ForbiddenSendIsABlockedChat()
    {
        TelegramSendOutcome outcome =
            await Client(new StubHandler(HttpStatusCode.Forbidden, body: """{"ok":false}""")).SendMessage(chatId: 42, "hi", CancellationToken.None);

        outcome.ShouldBe(TelegramSendOutcome.Blocked);
    }

    [Test]
    public async Task FailureNeverCarriesTheToken()
    {
        var failure = await Should.ThrowAsync<HttpRequestException>(() =>
            Client(new StubHandler(HttpStatusCode.InternalServerError, "")).SendMessage(chatId: 42, "hi", CancellationToken.None));

        failure.Message.ShouldNotContain(Token);
        failure.Message.ShouldNotContain("test-token");
    }

    [Test]
    public void StartLinkNeedsTheBotsUsername()
    {
        new TelegramOptions { BotUsername = "profitgraph_bot" }.StartLink("ABC").ShouldBe("https://t.me/profitgraph_bot?start=ABC");
        new TelegramOptions().StartLink("ABC").ShouldBeNull();
    }

    private static TelegramHttpClient Client(StubHandler handler)
    {
        return new TelegramHttpClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://telegram.invalid/") },
            Options.Create(new TelegramOptions { Enabled = true, BotToken = Token }));
    }
}