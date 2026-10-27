using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ProfitGraph.Tests.Integration.Notifications;

/// <summary>
///     The Telegram Bot API in the test process: it hands out pushed messages and button presses from the requested offset,
///     records sent messages with their buttons, callback answers and removed buttons, answers scripted failures to the next sends and refuses blocked chats. Nothing leaves the
///     process; the token
///     is invented.
/// </summary>
internal sealed class FakeTelegramApi : HttpMessageHandler
{
    public const string Token = "000000:test-telegram-token-not-real";

    private readonly ConcurrentQueue<(long UpdateId, object Update)> _updates = new ConcurrentQueue<(long, object)>();
    private readonly ConcurrentQueue<(HttpStatusCode Status, string Body)> _answers = new ConcurrentQueue<(HttpStatusCode, string)>();
    private long _nextUpdateId = 100;

    public ConcurrentQueue<(long ChatId, string Text)> Sent { get; } = new ConcurrentQueue<(long, string)>();
    public ConcurrentQueue<string?> ParseModes { get; } = new ConcurrentQueue<string?>();

    /// <summary>
    ///     The callback data of the buttons under each sent message, in order.
    /// </summary>
    public ConcurrentQueue<string[]> Buttons { get; } = new ConcurrentQueue<string[]>();

    public ConcurrentQueue<(string CallbackId, string? Text)> CallbackAnswers { get; } = new ConcurrentQueue<(string, string?)>();
    public ConcurrentQueue<(long ChatId, long MessageId)> RemovedButtons { get; } = new ConcurrentQueue<(long, long)>();
    public ConcurrentQueue<long> Offsets { get; } = new ConcurrentQueue<long>();
    public ConcurrentQueue<string> Paths { get; } = new ConcurrentQueue<string>();

    /// <summary>
    ///     Chats whose sends answer 403, as when the user blocked the bot.
    /// </summary>
    public ConcurrentDictionary<long, bool> Blocked { get; } = new ConcurrentDictionary<long, bool>();

    public void Push(long chatId, string text)
    {
        long updateId = Interlocked.Increment(ref _nextUpdateId);
        _updates.Enqueue(
            (updateId, new { update_id = updateId, message = new { chat = new { id = chatId, type = "private" }, from = new { id = chatId, is_bot = false }, text } }));
    }

    /// <summary>
    ///     A press of a button with <paramref name="data" /> under message <paramref name="messageId" /> of a private chat.
    /// </summary>
    public string Press(long chatId, long messageId, string data)
    {
        long updateId = Interlocked.Increment(ref _nextUpdateId);
        var callbackId = $"callback-{updateId}";
        _updates.Enqueue(
            (updateId,
                new
                {
                    update_id = updateId,
                    callback_query = new
                    {
                        id = callbackId,
                        from = new { id = chatId, is_bot = false },
                        message = new { message_id = messageId, chat = new { id = chatId, type = "private" } },
                        data
                    }
                }));

        return callbackId;
    }

    /// <summary>
    ///     Answers the next sends, one each, with these failures after doing nothing.
    /// </summary>
    public void FailNextSends(params (HttpStatusCode Status, string Body)[] answers)
    {
        foreach ((HttpStatusCode Status, string Body) answer in answers)
        {
            _answers.Enqueue(answer);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;
        Paths.Enqueue(path);
        using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));

        if (path == $"/bot{Token}/getUpdates")
        {
            long offset = body.RootElement.GetProperty("offset").GetInt64();
            Offsets.Enqueue(offset);
            object[] result =
            [
                .. _updates.Where(update => update.UpdateId >= offset).Select(update => update.Update)
            ];

            if (result.Length == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
            }

            return Json(new { ok = true, result });
        }

        if (path == $"/bot{Token}/sendMessage")
        {
            long chatId = body.RootElement.GetProperty("chat_id").GetInt64();

            if (Blocked.ContainsKey(chatId))
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("""{"ok":false,"error_code":403}""") };
            }

            if (_answers.TryDequeue(out (HttpStatusCode Status, string Body) failure))
            {
                return new HttpResponseMessage(failure.Status) { Content = new StringContent(failure.Body) };
            }

            Sent.Enqueue((chatId, body.RootElement.GetProperty("text").GetString()!));
            ParseModes.Enqueue(body.RootElement.TryGetProperty("parse_mode", out JsonElement mode) ? mode.GetString() : null);
            Buttons.Enqueue(
                body.RootElement.TryGetProperty("reply_markup", out JsonElement markup)
                    ?
                    [
                        .. markup.GetProperty("inline_keyboard")
                            .EnumerateArray()
                            .SelectMany(row => row.EnumerateArray())
                            .Select(button => button.GetProperty("callback_data").GetString()!)
                    ]
                    : []);

            return Json(new { ok = true, result = new { message_id = 1 } });
        }

        if (path == $"/bot{Token}/answerCallbackQuery")
        {
            CallbackAnswers.Enqueue(
                (body.RootElement.GetProperty("callback_query_id").GetString()!,
                    body.RootElement.TryGetProperty("text", out JsonElement text) ? text.GetString() : null));

            return Json(new { ok = true, result = true });
        }

        if (path == $"/bot{Token}/editMessageReplyMarkup")
        {
            RemovedButtons.Enqueue((body.RootElement.GetProperty("chat_id").GetInt64(), body.RootElement.GetProperty("message_id").GetInt64()));

            return Json(new { ok = true, result = true });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(object value)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}