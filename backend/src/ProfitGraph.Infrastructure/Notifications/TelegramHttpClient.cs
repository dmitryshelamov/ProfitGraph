using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Options;

using ProfitGraph.Application.Notifications;
using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Application.Notifications.Ports;

namespace ProfitGraph.Infrastructure.Notifications;

/// <summary>
///     The Telegram Bot API over HTTP (ADR 0020). The token is part of every request path, so the client's factory logging
///     is removed and no exception carries the address. A 403 to a send is a blocked chat; any other failure throws.
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

    public async Task<TelegramSendOutcome> SendMessage(long chatId, string text, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(Method("sendMessage"), new { chat_id = chatId, text }, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return TelegramSendOutcome.Blocked;
        }

        await Result(response, cancellationToken);

        return TelegramSendOutcome.Sent;
    }

    /// <summary>
    ///     A token has a colon (<c>123456:ABC</c>), so the path starts with <c>./</c> to stay relative to the base address.
    /// </summary>
    private string Method(string name)
    {
        return $"./bot{options.Value.BotToken}/{name}";
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