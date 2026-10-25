using MediatR;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.GetAccountHistory;

internal sealed class GetAccountHistoryQueryHandler(
    IExchangeCredentialRepository credentials,
    IAccountBalanceStatusRepository statuses,
    IAccountSnapshotRepository snapshots) : IRequestHandler<GetAccountHistoryQuery, AccountHistoryInternal>
{
    public async Task<AccountHistoryInternal> Handle(GetAccountHistoryQuery query, CancellationToken cancellationToken)
    {
        ExchangeCredential credential = await credentials.GetOwned(query.CredentialId, query.OwnerId, cancellationToken);
        AccountBalanceStatus? status = await statuses.Get(credential.Id, cancellationToken);
        IReadOnlyList<AccountSnapshot> history = await snapshots.List(credential.Id, query.From, query.To, query.Limit, cancellationToken);

        return new AccountHistoryInternal(
            status?.LastAttemptAt,
            status?.LastSuccessAt,
            status?.FailureReason,
            status?.NextAttemptAt,
            [
                .. history.Select(snapshot => new AccountSnapshotInternal(
                    snapshot.CapturedAt,
                    snapshot.TotalValue,
                    snapshot.ValueCurrency,
                    snapshot.AvailableValue,
                    [.. snapshot.Coins.Select(coin => new AccountCoinBalanceInternal(coin.Coin, coin.Balance, coin.UsdValue))]))
            ]);
    }
}