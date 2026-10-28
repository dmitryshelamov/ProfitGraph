using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Options;

using ProfitGraph.Application.Notifications;
using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Application.Notifications.Ports;

namespace ProfitGraph.Infrastructure.Notifications;

/// <summary>
///     The Telegram Bot API over HTTP (ADR 0020, ADR 0042). The token is part of every request path, so the client's factory
///     logging is removed and no exception or error carries the address. Telegram's answers to a send are classified, never
///     thrown; reading updates throws on any failure. Updates are text messages and button presses.
/// </summary>
internal sealed class TelegramHttpClient(HttpClient http, IOptions<TelegramOptions> options) : ITelegramClient
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public async Task<IReadOnlyList<TelegramUpdateInternal>> GetUpdates(long offset, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            Method("getUpdates"),
            new { offset, timeout = (int)timeout.TotalSeconds, allowed_updates = new[] { "message", "callback_query" } },
            cancellationToken);
        JsonElement result = await Result(response, cancellationToken);

        return [.. result.EnumerateArray().Select(Update)];
    }

    public async Task<TelegramSendResult> SendMessage(long chatId, string html, IReadOnlyList<TelegramButtonInternal> buttons, CancellationToken cancellationToken)
    {
        object? markup = buttons.Count == 0
            ? null
            : new { inline_keyboard = new[] { buttons.Select(button => new { text = button.Text, callback_data = button.CallbackData }).ToArray() } };
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            Method("sendMessage"),
            new { chat_id = chatId, text = html, parse_mode = "HTML", link_preview_options = new { is_disabled = true }, reply_markup = markup },
            JsonOptions,
            cancellationToken);

        return await SendResult(response, cancellationToken);
    }

    /// <summary>
    ///     Telegram's refusal to answer a press, such as one too old, changes nothing, so it is not reported.
    /// </summary>
    public async Task AnswerCallback(string callbackId, string? text, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response =
            await http.PostAsJsonAsync(Method("answerCallbackQuery"), new { callback_query_id = callbackId, text }, JsonOptions, cancellationToken);
    }

    /// <summary>
    ///     Telegram's refusal, such as for a message that no longer has buttons, changes nothing, so it is not reported.
    /// </summary>
    public async Task RemoveButtons(long chatId, long messageId, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            Method("editMessageReplyMarkup"),
            new { chat_id = chatId, message_id = messageId, reply_markup = new { inline_keyboard = Array.Empty<object>() } },
            cancellationToken);
    }

    private static TelegramUpdateInternal Update(JsonElement update)
    {
        long updateId = update.GetProperty("update_id").GetInt64();

        if (update.TryGetProperty("message", out JsonElement message) && message.TryGetProperty("chat", out JsonElement chat))
        {
            return new TelegramUpdateInternal(
                updateId,
                new TelegramIncomingMessageInternal(
                    chat.GetProperty("id").GetInt64(),
                    Text(chat, "type") ?? string.Empty,
                    FromBot(message),
                    Text(message, "text")));
        }

        if (update.TryGetProperty("callback_query", out JsonElement callback) && Text(callback, "id") is { } id)
        {
            JsonElement? source = callback.TryGetProperty("message", out JsonElement value) ? value : null;
            JsonElement? sourceChat = source is { } found && found.TryGetProperty("chat", out JsonElement chatValue) ? chatValue : null;

            return new TelegramUpdateInternal(
                updateId,
                Callback: new TelegramCallbackInternal(
                    id,
                    FromBot(callback),
                    sourceChat?.GetProperty("id").GetInt64(),
                    sourceChat is { } typed ? Text(typed, "type") : null,
                    source is { } withId && withId.TryGetProperty("message_id", out JsonElement messageId) ? messageId.GetInt64() : null,
                    Text(callback, "data")));
        }

        return new TelegramUpdateInternal(updateId);
    }

    private static bool FromBot(JsonElement element)
    {
        return element.TryGetProperty("from", out JsonElement from) && from.TryGetProperty("is_bot", out JsonElement isBot) && isBot.ValueKind == JsonValueKind.True;
    }

    private static string? Text(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>
    ///     A token has a colon (<c>123456:ABC</c>), so the path starts with <c>./</c> to stay relative to the base address.
    /// </summary>
    private string Method(string name)
    {
        return $"./bot{options.Value.BotToken}/{name}";
    }

    /// <summary>
    ///     Classifies Telegram's answer to a send (ADR 0042): a 403 is a blocked chat, a 400 a permanent refusal, anything else
    ///     unsuccessful is tried again after Telegram's <c>retry_after</c> when it gave one.
    /// </summary>
    private static async Task<TelegramSendResult> SendResult(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return TelegramSendResult.Sent;
        }

        (string? description, TimeSpan? retryAfter) = await Error(response, cancellationToken);
        var code = $"http_{(int)response.StatusCode}";
        string error = description is null ? code : $"{code}: {description}";

        return response.StatusCode switch
        {
            HttpStatusCode.Forbidden => new TelegramSendResult(TelegramSendOutcome.Blocked, Error: error),
            HttpStatusCode.BadRequest => new TelegramSendResult(TelegramSendOutcome.Rejected, Error: error),
            _ => new TelegramSendResult(TelegramSendOutcome.RetryLater, retryAfter, error)
        };
    }

    /// <summary>
    ///     Telegram's description and <c>parameters.retry_after</c> from an error answer; nothing from a body that is not
    ///     Telegram's JSON.
    /// </summary>
    private static async Task<(string? Description, TimeSpan? RetryAfter)> Error(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            JsonElement root = document.RootElement;
            string? description = root.TryGetProperty("description", out JsonElement text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
            TimeSpan? retryAfter = root.TryGetProperty("parameters", out JsonElement parameters) && parameters.ValueKind == JsonValueKind.Object &&
                parameters.TryGetProperty("retry_after", out JsonElement seconds) && seconds.TryGetInt32(out int value) && value > 0
                    ? TimeSpan.FromSeconds(value)
                    : null;

            return (description, retryAfter);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static async Task<JsonElement> Result(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Telegram answered {(int)response.StatusCode}.", inner: null, response.StatusCode);
        }

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return document.RootElement.TryGetProperty("ok", out JsonElement ok) && ok.GetBoolean() && document.RootElement.TryGetProperty("result", out JsonElement result)
            ? result.Clone()
            : throw new InvalidDataException("Telegram answered without a result.");
    }
}