namespace ProfitGraph.Application.Accounts.Contracts;

/// <summary>
///     An account's value as the venue reports it: the USD-equivalent total, the available value when known, and coins.
/// </summary>
public sealed record AccountBalanceObservationInternal(
    decimal TotalValueUsd,
    decimal? AvailableValueUsd,
    IReadOnlyList<AccountCoinBalanceInternal> Coins,
    DateTimeOffset ObservedAt);