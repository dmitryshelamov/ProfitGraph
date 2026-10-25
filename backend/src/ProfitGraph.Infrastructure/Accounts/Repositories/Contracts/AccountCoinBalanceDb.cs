namespace ProfitGraph.Infrastructure.Accounts.Repositories.Contracts;

/// <summary>
///     A coin of a stored snapshot; amounts are exact decimal strings.
/// </summary>
internal sealed class AccountCoinBalanceDb
{
    public string Coin { get; init; } = string.Empty;
    public string Balance { get; init; } = "0";
    public string? UsdValue { get; init; }
}