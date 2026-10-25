using System.Text.Json;

using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     Signed Bybit reads of a bot's orders and key (Profit Fox Designer ADR 0055). An answer that cannot be read is
///     <see cref="BybitOrderUnresolvedException" />: it proves nothing, so the caller retries.
/// </summary>
internal sealed class BybitOrderReads(BybitReadClient read)
{
    /// <summary>
    ///     Finds an order of the bot by its client ID in open, then in historical orders; null when neither knows it, which
    ///     is not proof that the order was never placed.
    /// </summary>
    public async Task<BybitSpotOrder?> LookUp(BybitBotContext context, string clientOrderId, CancellationToken cancellationToken)
    {
        BybitSpotOrder? order = await Find(context, "/v5/order/realtime", clientOrderId, cancellationToken)
            ?? await Find(context, "/v5/order/history", clientOrderId, cancellationToken);

        if (order is not null && (!BybitClientOrderIds.TryParse(order.ClientOrderId, out BotId? owner, out _) || owner != context.BotId ||
                order.Symbol != context.Symbol))
        {
            throw new BybitOrderUnresolvedException();
        }

        return order;
    }

    /// <summary>
    ///     Whether the key may trade Spot and cannot withdraw. A key Bybit refuses is not allowed; an unreadable answer
    ///     throws.
    /// </summary>
    public async Task<bool> KeyMayTrade(BybitBotContext context, CancellationToken cancellationToken)
    {
        BybitRestResult key = await read.Get(context.Credential, "/v5/user/query-api", string.Empty, cancellationToken);

        if (key is { Outcome: BybitRestOutcome.Unknown, Code: 10003 or 10005 or 10010 })
        {
            return false;
        }

        if (key.Outcome != BybitRestOutcome.Accepted)
        {
            throw new BybitOrderUnresolvedException();
        }

        try
        {
            JsonElement result = key.Body.GetProperty("result");
            JsonElement permissions = result.GetProperty("permissions");
            bool canTrade = permissions.GetProperty("Spot").EnumerateArray().Any(item => item.GetString() == "SpotTrade");
            bool canWithdraw = permissions.TryGetProperty("Wallet", out JsonElement wallet) && wallet.EnumerateArray().Any(item => item.GetString() == "Withdraw");

            return result.GetProperty("readOnly").GetInt32() == 0 && canTrade && !canWithdraw;
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new BybitOrderUnresolvedException();
        }
    }

    /// <summary>
    ///     The client IDs of the open orders and the recent executions on the bot's symbol.
    /// </summary>
    public async Task<IReadOnlyList<string>> SymbolClientOrderIds(BybitBotContext context, CancellationToken cancellationToken)
    {
        string symbol = Uri.EscapeDataString(context.Symbol);
        List<string> ids = [];
        ids.AddRange(await List(context, "/v5/order/realtime", $"category=spot&symbol={symbol}&openOnly=0&limit=50", cancellationToken));
        ids.AddRange(await List(context, "/v5/execution/list", $"category=spot&symbol={symbol}&limit=100", cancellationToken));

        return ids;
    }

    private async Task<BybitSpotOrder?> Find(BybitBotContext context, string path, string clientOrderId, CancellationToken cancellationToken)
    {
        BybitRestResult found = await read.Get(context.Credential, path, $"category=spot&orderLinkId={Uri.EscapeDataString(clientOrderId)}", cancellationToken);

        if (found.Outcome != BybitRestOutcome.Accepted)
        {
            return found.Outcome == BybitRestOutcome.NotFound ? null : throw new BybitOrderUnresolvedException();
        }

        try
        {
            JsonElement list = found.Body.GetProperty("result").GetProperty("list");

            return list.GetArrayLength() == 0
                ? null
                : BybitSpotOrder.Parse(list[0]) is { } order && order.ClientOrderId == clientOrderId
                    ? order
                    : throw new BybitOrderUnresolvedException();
        }
        catch (Exception exception) when (exception is KeyNotFoundException or FormatException or InvalidOperationException)
        {
            throw new BybitOrderUnresolvedException();
        }
    }

    /// <summary>
    ///     Every page of a list; a repeated cursor or an unreadable page throws.
    /// </summary>
    private async Task<IReadOnlyList<string>> List(BybitBotContext context, string path, string query, CancellationToken cancellationToken)
    {
        List<string> ids = [];
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        var cursor = string.Empty;

        do
        {
            string page = cursor.Length == 0 ? query : $"{query}&cursor={Uri.EscapeDataString(cursor)}";
            BybitRestResult listed = await read.Get(context.Credential, path, page, cancellationToken);

            if (listed.Outcome != BybitRestOutcome.Accepted)
            {
                throw new BybitOrderUnresolvedException();
            }

            try
            {
                JsonElement result = listed.Body.GetProperty("result");
                ids.AddRange(result.GetProperty("list").EnumerateArray().Select(item => item.GetProperty("orderLinkId").GetString() ?? string.Empty));
                cursor = result.TryGetProperty("nextPageCursor", out JsonElement next) ? next.GetString() ?? string.Empty : string.Empty;
            }
            catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
            {
                throw new BybitOrderUnresolvedException();
            }

            if (cursor.Length > 0 && !cursors.Add(cursor))
            {
                throw new BybitOrderUnresolvedException();
            }
        }
        while (cursor.Length > 0);

        return ids;
    }
}