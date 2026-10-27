using MediatR;

using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.GetBotAccount;

/// <summary>
///     Reads the database and the venue modules' own stored state only, never a venue: the account's identity comes from the
///     cache the Worker fills (ADR 0049). A credential's public identifier is shown only when it names the account; a venue
///     whose public field is the API key shows the key's last four characters instead.
/// </summary>
internal sealed class GetBotAccountQueryHandler(
    IBotRepository bots,
    IExchangeCredentialRepository credentials,
    IAccountBalanceStatusRepository statuses,
    IAccountSnapshotRepository snapshots,
    IAccountIdentityRepository identities,
    VenuePorts<IVenueCredentialSchema> schemas,
    VenuePorts<IVenueAccountStatus> venueStatuses,
    VenuePorts<IVenueAccountProtection> protections) : IRequestHandler<GetBotAccountQuery, BotAccountInternal>
{
    public async Task<BotAccountInternal> Handle(GetBotAccountQuery query, CancellationToken cancellationToken)
    {
        Bot bot = await bots.GetOwned(query.BotId, query.OwnerId, cancellationToken);
        ExchangeCredential credential = await credentials.GetOwned(bot.CredentialId.Value, query.OwnerId, cancellationToken);
        AccountBalanceStatus? status = await statuses.Get(credential.Id, cancellationToken);
        AccountSnapshot? snapshot = await snapshots.Latest(credential.Id, cancellationToken);
        AccountIdentity? identity = await identities.Get(credential.Id, cancellationToken);
        VenueAccountStatusInternal? venueStatus = venueStatuses.Find(credential.Venue) is { } venue ? await venue.Read(credential.Id.Value, cancellationToken) : null;
        VenueAccountProtectionInternal protection = protections.Find(credential.Venue) is { } guard
            ? await guard.Read(credential.Id.Value, cancellationToken)
            : VenueAccountProtectionInternal.Clear;
        IReadOnlyList<Bot> owned = await bots.ListByOwner(bot.OwnerId, cancellationToken);
        bool publicIdNamesAccount = schemas.Find(credential.Venue) is { } schema &&
            !string.Equals(schema.PublicField, ExchangeCredentialFields.ApiKey, StringComparison.Ordinal);

        return new BotAccountInternal(
            credential.Id.Value,
            credential.Venue.Value,
            credential.Label.Value,
            credential.ApiKeyLast4.Value,
            publicIdNamesAccount ? credential.PublicId?.Value : null,
            snapshot is null
                ? null
                : new AccountSnapshotInternal(
                    snapshot.CapturedAt,
                    snapshot.TotalValue,
                    snapshot.ValueCurrency,
                    snapshot.AvailableValue,
                    [.. snapshot.Coins.Select(coin => new AccountCoinBalanceInternal(coin.Coin, coin.Balance, coin.UsdValue))]),
            status?.LastAttemptAt,
            status?.LastSuccessAt,
            status?.FailureReason,
            status?.NextAttemptAt,
            identity?.Details is { } details
                ? new AccountIdentityInternal(
                    details.AccountId,
                    details.Name,
                    details.Kind,
                    details.AccessLevel,
                    details.ReadOnly,
                    details.Permissions,
                    details.ExpiresAt)
                : null,
            identity?.ReadAt,
            identity?.FailureReason,
            venueStatus,
            protection,
            bot.AllocationCap,
            [
                .. owned
                    .Where(other => other.CredentialId == bot.CredentialId && other.Id != bot.Id && other.DesiredState == BotDesiredState.Running)
                    .Select(other => new BotAllocationInternal(other.Id.Value, other.Name?.Value, other.Series.Symbol.Value, other.AllocationCap, other.RuntimeStatus))
            ]);
    }
}