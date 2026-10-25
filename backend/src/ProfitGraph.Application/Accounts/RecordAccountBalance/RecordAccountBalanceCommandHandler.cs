using MediatR;

using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.RecordAccountBalance;

internal sealed class RecordAccountBalanceCommandHandler(IAccountBalanceStatusRepository statuses, IAccountSnapshotRepository snapshots, TimeProvider clock)
    : IRequestHandler<RecordAccountBalanceCommand, bool>
{
    public async Task<bool> Handle(RecordAccountBalanceCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        var credentialId = new ExchangeCredentialId(command.CredentialId);
        AccountBalanceStatus status = await statuses.GetForUpdate(credentialId, cancellationToken) ?? AccountBalanceStatus.New(credentialId);
        var stored = false;

        if (command.Read.Observation is { } observation)
        {
            status.RecordSuccess(now);
            stored = await snapshots.TryAdd(
                AccountSnapshot.Capture(
                    AccountSnapshotId.New(),
                    credentialId,
                    observation.ObservedAt,
                    observation.TotalValueUsd,
                    observation.AvailableValueUsd,
                    [.. observation.Coins.Select(coin => new AccountCoinBalance(coin.Coin, coin.Balance, coin.UsdValue))]),
                cancellationToken);
        }
        else
        {
            status.RecordFailure(command.Read.FailureReason ?? "unknown", command.Read.FailureDetail, now);
        }

        await statuses.Save(status, cancellationToken);

        return stored;
    }
}