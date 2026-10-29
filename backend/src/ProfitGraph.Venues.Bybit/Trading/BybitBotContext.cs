using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     What a Bybit call for a bot needs: its symbol and rules, and its credential decrypted in memory. A USDT-perpetual bot
///     (ADR 0061) also has its market, the direction of its strategy and its leverage; a Spot bot is long at 1×.
/// </summary>
internal sealed record BybitBotContext(BotId BotId, string Symbol, InstrumentRulesDefinitionInternal Instrument, ExchangeCredentialSecretsInternal Credential)
{
    public BybitMarket Market { get; init; } = BybitMarket.Spot;

    public PositionDirection Direction { get; init; } = PositionDirection.Long;

    public decimal Leverage { get; init; } = 1m;

    /// <summary>
    ///     The hedge-mode side of the bot's position: 1 for a long, 2 for a short.
    /// </summary>
    public int PositionIndex => Direction == PositionDirection.Long ? 1 : 2;

    /// <summary>
    ///     Whether an order of the side only closes the bot's position.
    /// </summary>
    public bool Reduces(OrderSide side)
    {
        return side != (Direction == PositionDirection.Long ? OrderSide.Buy : OrderSide.Sell);
    }
}