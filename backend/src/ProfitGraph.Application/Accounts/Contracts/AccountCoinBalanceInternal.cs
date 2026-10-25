namespace ProfitGraph.Application.Accounts.Contracts;

public sealed record AccountCoinBalanceInternal(string Coin, decimal Balance, decimal? UsdValue);