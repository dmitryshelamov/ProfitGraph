namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     A USDT perpetual's position as Bybit reports it (ADR 0061): whether the symbol is in hedge mode, the size of the bot's side
///     there (positive for a long, negative for a short), and in one-way mode the symbol's one net position, signed the same way.
/// </summary>
internal sealed record BybitLinearPosition(bool Hedge, decimal Side, decimal Net)
{
    /// <summary>
    ///     What the bot's own position is compared with: its side in hedge mode, the net position in one-way mode.
    /// </summary>
    public decimal Held => Hedge ? Side : Net;
}