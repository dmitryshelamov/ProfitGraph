namespace ProfitGraph.Application.Accounts.Contracts;

/// <summary>
///     An account's value as the venue reports it, in the currency it values the account in (USD for an exchange, a
///     marketplace's wallet currency; ADR 0022): the total, the available value when known, and coins.
/// </summary>
public sealed record AccountBalanceObservationInternal(
    decimal TotalValue,
    string ValueCurrency,
    decimal? AvailableValue,
    IReadOnlyList<AccountCoinBalanceInternal> Coins,
    DateTimeOffset ObservedAt);