namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     A venue's last-trade price of a symbol and when the venue observed it.
/// </summary>
public sealed record LiveMarkPriceInternal(decimal Price, DateTimeOffset ObservedAt);