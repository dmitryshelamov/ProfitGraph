namespace ProfitGraph.Domain.Accounts.Snapshots;

/// <summary>
///     One coin of an account snapshot: its wallet balance and, when the venue values it, its USD value.
/// </summary>
public sealed record AccountCoinBalance(string Coin, decimal Balance, decimal? UsdValue);