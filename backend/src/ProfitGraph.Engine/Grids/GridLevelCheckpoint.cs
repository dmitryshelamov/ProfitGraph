namespace ProfitGraph.Engine.Grids;

/// <summary>
///     One level of a grid between roots: the base it holds net of the buy fee, the quote it paid for it, and whether a
///     rejection holds it until the next candle close.
/// </summary>
public sealed record GridLevelCheckpoint(int Index, decimal HeldBase, decimal PaidQuote, bool Blocked);