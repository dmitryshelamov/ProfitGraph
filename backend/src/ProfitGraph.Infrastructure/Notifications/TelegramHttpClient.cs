using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Options;

using ProfitGraph.Application.Notifications;
using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Application.Notifications.Ports;

namespace ProfitGraph.Infrastructure.Notifications;

/// <summary>
///     The Telegram Bot API over HTTP (ADR 0020, ADR 0042). The token is part of every request path, so the client's factory
///     logging is removed and no exception or error carries the address. Telegram's answers to a send are classified, never
///     thrown; reading updates throws on any failure.
/// </summary>
internal sealed class TelegramHttpClient(HttpClient http, IOptions<TelegramOptions> options) : ITelegramClient
{
    public async Task<IReadOnlyList<TelegramUpdateInternal>> GetUpdates(long offset, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            Method("getUpdates"),
            new { offset, timeout = (int)timeout.TotalSeconds, allowed_updates = new[] { "message" } },
            cancellationToken);
        JsonElement result = await Result(response, cancellationToken);

        return
        [
            .. result.EnumerateArray()
                .Select(update => new TelegramUpdateInternal(
                    update.GetProperty("update_id").GetInt64(),
                    update.TryGetProperty("message", out JsonElement message) && message.TryGetProperty("chat", out JsonElement chat)
                        ? chat.GetProperty("id").GetInt64()
                        : null,
                    update.TryGetProperty("message", out JsonElement text) && text.TryGetProperty("text", out JsonElement value) ? value.GetString() : null))
        ];
    }

    public async Task<TelegramSendResult> SendMessage(long chatId, string html, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            Method("sendMessage"),
            new { chat_id = chatId, text = html, parse_mode = "HTML", link_preview_options = new { is_disabled = true } },
            cancellationToken);

        return await SendResult(response, cancellationToken);
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