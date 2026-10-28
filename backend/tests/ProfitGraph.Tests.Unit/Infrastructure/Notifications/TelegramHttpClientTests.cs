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
            body: """
                  {"ok":true,"result":[
                    {"update_id":7,"message":{"chat":{"id":42,"type":"private"},"from":{"id":42,"is_bot":false},"text":"/start ABC"}},
                    {"update_id":8,"edited_message":{}},
                    {"update_id":9,"callback_query":{"id":"cb1","from":{"id":42,"is_bot":false},"message":{"message_id":5,"chat":{"id":42,"type":"private"}},"data":"ack:x"}},
                    {"update_id":10,"message":{"chat":{"id":-100,"type":"group"},"from":{"id":7,"is_bot":true}}}
                  ]}
                  """);

        IReadOnlyList<TelegramUpdateInternal> updates = await Client(handler).GetUpdates(offset: 7, TimeSpan.FromSeconds(1), CancellationToken.None);

        handler.Path.ShouldBe($"/bot{Token}/getUpdates");
        handler.Body!.ShouldContain("\"allowed_updates\":[\"message\",\"callback_query\"]");
        updates.ShouldBe(
        [
            new TelegramUpdateInternal(UpdateId: 7, new TelegramIncomingMessageInternal(ChatId: 42, "private", FromBot: false, "/start ABC")),
            new TelegramUpdateInternal(UpdateId: 8),
            new TelegramUpdateInternal(UpdateId: 9, Callback: new TelegramCallbackInternal("cb1", FromBot: false, ChatId: 42, "private", MessageId: 5, "ack:x")),
            new TelegramUpdateInternal(UpdateId: 10, new TelegramIncomingMessageInternal(ChatId: -100, "group", FromBot: true, Text: null))
        ]);
    }

    [Test]
    public async Task ButtonsGoInOneRowUnderTheMessage()
    {
        var handler = new StubHandler(HttpStatusCode.OK, body: """{"ok":true,"result":{"message_id":1}}""");

        await Client(handler).SendMessage(chatId: 42, "hi", [new TelegramButtonInternal("Принято", "ack:1")], CancellationToken.None);

        handler.Body!.ShouldContain(
            "\"reply_markup\":{\"inline_keyboard\":[[{\"text\":\"\\u041F\\u0440\\u0438\\u043D\\u044F\\u0442\\u043E\",\"callback_data\":\"ack:1\"}]]}");
    }

    [Test]
    public async Task MessageWithoutButtonsHasNoMarkup()
    {
        var handler = new StubHandler(HttpStatusCode.OK, body: """{"ok":true,"result":{"message_id":1}}""");

        await Client(handler).SendMessage(chatId: 42, "hi", [], CancellationToken.None);

        handler.Body!.ShouldNotContain("reply_markup");
    }

    [Test]
    public async Task RefusedCallbackAnswerAndButtonRemovalAreNotFailures()
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest, body: """{"ok":false,"description":"Bad Request: query is too old"}""");

        await Client(handler).AnswerCallback("cb1", "Принято", CancellationToken.None);
        handler.Path.ShouldBe($"/bot{Token}/answerCallbackQuery");
        await Client(handler).RemoveButtons(chatId: 42, messageId: 5, CancellationToken.None);
        handler.Path.ShouldBe($"/bot{Token}/editMessageReplyMarkup");
        handler.Body!.ShouldContain("\"inline_keyboard\":[]");
    }

    [Test]
    public async Task ForbiddenSendIsABlockedChat()
    {
        TelegramSendResult result = await Client(
                new StubHandler(HttpStatusCode.Forbidden, body: """{"ok":false,"error_code":403,"description":"Forbidden: bot was blocked by the user"}"""))
            .SendMessage(chatId: 42, "hi", [], CancellationToken.None);

        result.ShouldBe(new TelegramSendResult(TelegramSendOutcome.Blocked, Error: "http_403: Forbidden: bot was blocked by the user"));
    }

    [Test]
    public async Task SendIsHtmlToTheTokensPath()
    {
        var handler = new StubHandler(HttpStatusCode.OK, body: """{"ok":true,"result":{"message_id":1}}""");

        TelegramSendResult result = await Client(handler).SendMessage(chatId: 42, "<b>hi</b>", [], CancellationToken.None);

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
            .SendMessage(chatId: 42, "hi", [], CancellationToken.None);

        result.ShouldBe(new TelegramSendResult(TelegramSendOutcome.RetryLater, TimeSpan.FromSeconds(30), "http_429: Too Many Requests: retry after 30"));
    }

    [Test]
    public async Task BadRequestIsAPermanentRefusal()
    {
        TelegramSendResult result = await Client(
                new StubHandler(HttpStatusCode.BadRequest, body: """{"ok":false,"error_code":400,"description":"Bad Request: can't parse entities"}"""))
            .SendMessage(chatId: 42, "<b>hi", [], CancellationToken.None);

        result.ShouldBe(new TelegramSendResult(TelegramSendOutcome.Rejected, Error: "http_400: Bad Request: can't parse entities"));
    }

    [Test]
    public async Task ServerErrorAndWrongTokenAreTriedAgain()
    {
        TelegramSendResult failure = await Client(new StubHandler(HttpStatusCode.BadGateway, "<html>bad gateway</html>"))
            .SendMessage(chatId: 42, "hi", [], CancellationToken.None);
        TelegramSendResult unauthorized = await Client(new StubHandler(HttpStatusCode.Unauthorized, body: """{"ok":false,"description":"Unauthorized"}"""))
            .SendMessage(chatId: 42, "hi", [], CancellationToken.None);

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