using System.Globalization;
using System.Text.Json;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

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
    ///     Whether the key may trade the bot's market and cannot withdraw: Spot trading, or for USDT perpetuals (ADR 0061) contract
    ///     orders and positions. A key Bybit refuses is not allowed; an unreadable answer throws.
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
            bool canTrade = context.Market.IsLinear
                ? permissions.TryGetProperty("ContractTrade", out JsonElement contract) &&
                contract.EnumerateArray().Select(item => item.GetString()).ToHashSet(StringComparer.Ordinal) is var granted && granted.Contains("Order") &&
                granted.Contains("Position")
                : permissions.GetProperty("Spot").EnumerateArray().Any(item => item.GetString() == "SpotTrade");
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
        ids.AddRange(await List(context, "/v5/order/realtime", $"category={context.Market.Name}&symbol={symbol}&openOnly=0&limit=50", cancellationToken));
        ids.AddRange(await List(context, "/v5/execution/list", $"category={context.Market.Name}&symbol={symbol}&limit=100", cancellationToken));

        return ids;
    }

    /// <summary>
    ///     Whether the symbol's execution list between two instants holds an execution of the order (ADR 0030). Every page is
    ///     read; an unreadable or repeated page throws.
    /// </summary>
    public async Task<bool> ExecutedBetween(
        BybitBotContext context,
        string clientOrderId,
        DateTimeOffset since,
        DateTimeOffset until,
        CancellationToken cancellationToken)
    {
        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"category={context.Market.Name}&symbol={Uri.EscapeDataString(context.Symbol)}&startTime={since.ToUnixTimeMilliseconds()}&endTime={until.ToUnixTimeMilliseconds()}&limit=100");

        return (await List(context, "/v5/execution/list", query, cancellationToken)).Contains(clientOrderId, StringComparer.Ordinal);
    }

    /// <summary>
    ///     The wallet balance of the symbol's base coin on the UNIFIED account (ADR 0030); zero when the account lists no such
    ///     coin. A coin listed twice or an unreadable answer throws.
    /// </summary>
    public async Task<decimal> BaseWalletBalance(BybitBotContext context, CancellationToken cancellationToken)
    {
        BybitRestResult balance = await read.Get(
            context.Credential,
            "/v5/account/wallet-balance",
            $"accountType=UNIFIED&coin={Uri.EscapeDataString(context.Instrument.BaseAsset)}",
            cancellationToken);

        if (balance.Outcome != BybitRestOutcome.Accepted)
        {
            throw new BybitOrderUnresolvedException();
        }

        try
        {
            JsonElement account = balance.Body.GetProperty("result").GetProperty("list")[0];
            JsonElement[] coins =
            [
                .. account.GetProperty("coin")
                    .EnumerateArray()
                    .Where(coin => coin.TryGetProperty("coin", out JsonElement name) && name.GetString() == context.Instrument.BaseAsset)
            ];

            return coins.Length switch
            {
                0 => 0m,
                1 => decimal.Parse(coins[0].GetProperty("walletBalance").GetString()!, NumberStyles.Float, CultureInfo.InvariantCulture),
                _ => throw new BybitOrderUnresolvedException()
            };
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException or IndexOutOfRangeException)
        {
            throw new BybitOrderUnresolvedException();
        }
    }

    /// <summary>
    ///     The bot's side of a USDT perpetual in hedge mode (ADR 0061): its size, positive for a long and negative for a short, or
    ///     null when the symbol is in one-way mode. An unreadable answer throws.
    /// </summary>
    public async Task<decimal?> HedgePosition(BybitBotContext context, CancellationToken cancellationToken)
    {
        BybitRestResult listed = await read.Get(
            context.Credential,
            "/v5/position/list",
            $"category=linear&symbol={Uri.EscapeDataString(context.Symbol)}",
            cancellationToken);

        if (listed.Outcome != BybitRestOutcome.Accepted)
        {
            throw new BybitOrderUnresolvedException();
        }

        try
        {
            JsonElement[] positions = [.. listed.Body.GetProperty("result").GetProperty("list").EnumerateArray()];

            if (positions.Any(position => position.GetProperty("positionIdx").GetInt32() == 0))
            {
                return null;
            }

            JsonElement[] side = [.. positions.Where(position => position.GetProperty("positionIdx").GetInt32() == context.PositionIndex)];

            if (side.Length > 1)
            {
                throw new BybitOrderUnresolvedException();
            }

            decimal size = side.Length == 0 || side[0].GetProperty("size").GetString() is not { Length: > 0 } text
                ? 0m
                : decimal.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

            return context.Direction == PositionDirection.Long ? size : -size;
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new BybitOrderUnresolvedException();
        }
    }

    private async Task<BybitSpotOrder?> Find(BybitBotContext context, string path, string clientOrderId, CancellationToken cancellationToken)
    {
        BybitRestResult found = await read.Get(
            context.Credential,
            path,
            $"category={context.Market.Name}&orderLinkId={Uri.EscapeDataString(clientOrderId)}",
            cancellationToken);

        if (found.Outcome != BybitRestOutcome.Accepted)
        {
            return found.Outcome == BybitRestOutcome.NotFound ? null : throw new BybitOrderUnresolvedException();
        }

        try
        {
            JsonElement list = found.Body.GetProperty("result").GetProperty("list");

            return list.GetArrayLength() == 0
                ? null
                : BybitSpotOrder.Parse(list[0], context.Market.IsLinear ? context.Instrument.QuoteAsset : null) is { } order &&
                order.ClientOrderId == clientOrderId
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

                // A USDT perpetual's funding is an execution of no order (ADR 0061).
                ids.AddRange(
                    result.GetProperty("list")
                        .EnumerateArray()
                        .Where(item => !IsFunding(item))
                        .Select(item => item.GetProperty("orderLinkId").GetString() ?? string.Empty));
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

    internal static bool IsFunding(JsonElement item)
    {
        return item.TryGetProperty("execType", out JsonElement kind) && kind.GetString() == "Funding";
    }
}