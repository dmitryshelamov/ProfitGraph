using System.Net;

using Microsoft.Extensions.Options;

using ProfitGraph.Application.Notifications;
using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Infrastructure.Notifications;

namespace ProfitGraph.Tests.Unit.Infrastructure.Notifications;

/// <summary>
///     The Telegram HTTP client (ADR 0020, ADR 0042): its paths stay relative although a token has a colon, sends are HTML,
///     Telegram's answers are classified (403 blocked, 400 refused, anything else tried again after <c>retry_after</c>), and
///     no failure carries the token. The token is invented.
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
        TelegramSendResult result = await Client(
                new StubHandler(HttpStatusCode.Forbidden, body: """{"ok":false,"error_code":403,"description":"Forbidden: bot was blocked by the user"}"""))
            .SendMessage(chatId: 42, "hi", CancellationToken.None);

        result.ShouldBe(new TelegramSendResult(TelegramSendOutcome.Blocked, Error: "http_403: Forbidden: bot was blocked by the user"));
    }

    [Test]
    public async Task SendIsHtmlToTheTokensPath()
    {
        var handler = new StubHandler(HttpStatusCode.OK, body: """{"ok":true,"result":{"message_id":1}}""");

        TelegramSendResult result = await Client(handler).SendMessage(chatId: 42, "<b>hi</b>", CancellationToken.None);

        result.ShouldBe(TelegramSendResult.Sent);
        handler.Path.ShouldBe($"/bot{Token}/sendMessage");
        handler.Body!.ShouldContain("\"parse_mode\":\"HTML\"");
    }

    [Test]
    public async Task TooManyRequestsWaitsForTelegramsRetryAfter()
    {
        TelegramSendResult result = await Client(
                new StubHandler(
                    HttpStatusCode.TooManyRequests,
                    body: """{"ok":false,"error_code":429,"description":"Too Many Requests: retry after 30","parameters":{"retry_after":30}}"""))
            .SendMessage(chatId: 42, "hi", CancellationToken.None);

        result.ShouldBe(new TelegramSendResult(TelegramSendOutcome.RetryLater, TimeSpan.FromSeconds(30), "http_429: Too Many Requests: retry after 30"));
    }

    [Test]
    public async Task BadRequestIsAPermanentRefusal()
    {
        TelegramSendResult result = await Client(
                new StubHandler(HttpStatusCode.BadRequest, body: """{"ok":false,"error_code":400,"description":"Bad Request: can't parse entities"}"""))
            .SendMessage(chatId: 42, "<b>hi", CancellationToken.None);

        result.ShouldBe(new TelegramSendResult(TelegramSendOutcome.Rejected, Error: "http_400: Bad Request: can't parse entities"));
    }

    [Test]
    public async Task ServerErrorAndWrongTokenAreTriedAgain()
    {
        TelegramSendResult failure = await Client(new StubHandler(HttpStatusCode.BadGateway, "<html>bad gateway</html>"))
            .SendMessage(chatId: 42, "hi", CancellationToken.None);
        TelegramSendResult unauthorized = await Client(new StubHandler(HttpStatusCode.Unauthorized, body: """{"ok":false,"description":"Unauthorized"}"""))
            .SendMessage(chatId: 42, "hi", CancellationToken.None);

        failure.ShouldBe(new TelegramSendResult(TelegramSendOutcome.RetryLater, Error: "http_502"));
        unauthorized.Outcome.ShouldBe(TelegramSendOutcome.RetryLater);
        unauthorized.Error!.ShouldNotContain(Token);
    }

    [Test]
    public async Task FailureNeverCarriesTheToken()
    {
        var failure = await Should.ThrowAsync<HttpRequestException>(() =>
            Client(new StubHandler(HttpStatusCode.InternalServerError, "")).GetUpdates(offset: 0, TimeSpan.Zero, CancellationToken.None));

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