using ProfitGraph.Api.Accounts.Contracts;
using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Application.Accounts.Contracts;

namespace ProfitGraph.Api.Accounts.Converters;

internal static class AccountHistoryResponseConverter
{
    public static AccountHistoryResponse ToResponse(this AccountHistoryInternal history)
    {
        return new AccountHistoryResponse(
            history.LastAttemptAt,
            history.LastSuccessAt,
            history.FailureReason,
            history.NextAttemptAt,
            [
                .. history.Snapshots.Select(snapshot => new AccountSnapshotResponse(
                    snapshot.CapturedAt,
                    ExactGraphNumberParser.Format(snapshot.TotalValueUsd),
                    snapshot.AvailableValueUsd is { } available ? ExactGraphNumberParser.Format(available) : null,
                    [
                        .. snapshot.Coins.Select(coin => new AccountCoinBalanceResponse(
                            coin.Coin,
                            ExactGraphNumberParser.Format(coin.Balance),
                            coin.UsdValue is { } usd ? ExactGraphNumberParser.Format(usd) : null))
                    ]))
            ]);
    }
}