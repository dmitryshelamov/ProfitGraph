namespace ProfitGraph.Api.Accounts.Contracts;

public sealed record AccountCoinBalanceResponse(string Coin, string Balance, string? UsdValue);