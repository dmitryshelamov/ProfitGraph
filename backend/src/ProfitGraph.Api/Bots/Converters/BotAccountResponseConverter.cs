using ProfitGraph.Api.Accounts.Contracts;
using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Bots.Contracts;

namespace ProfitGraph.Api.Bots.Converters;

internal static class BotAccountResponseConverter
{
    public static BotAccountResponse ToResponse(this BotAccountInternal account)
    {
        return new BotAccountResponse(
            account.CredentialId,
            account.Venue,
            account.CredentialLabel,
            account.ApiKeyLast4,
            account.AccountPublicId,
            account is { Identity: { } identity, IdentityReadAt: { } readAt }
                ? new BotAccountIdentityResponse(
                    identity.AccountId,
                    identity.Name,
                    identity.Kind,
                    identity.AccessLevel,
                    identity.ReadOnly,
                    identity.Permissions,
                    identity.ExpiresAt,
                    readAt)
                : null,
            account.IdentityFailureReason,
            account.Snapshot is { } snapshot ? Snapshot(snapshot) : null,
            account.LastAttemptAt,
            account.LastSuccessAt,
            account.FailureReason,
            account.NextAttemptAt,
            account.VenueStatus is { } status ? new AccountStatusResponse(status.Status, status.NextCheckAt) : null,
            new AccountProtectionResponse(account.Protection.Open, account.Protection.Reason, account.Protection.Since, account.Protection.AutomaticResumeAt),
            ExactGraphNumberParser.Format(account.AllocationCap),
            [
                .. account.OtherRunningBots.Select(bot => new BotAllocationResponse(
                    bot.BotId,
                    bot.Name,
                    bot.Symbol,
                    ExactGraphNumberParser.Format(bot.AllocationCap),
                    bot.RuntimeStatus))
            ]);
    }

    private static AccountSnapshotResponse Snapshot(AccountSnapshotInternal snapshot)
    {
        return new AccountSnapshotResponse(
            snapshot.CapturedAt,
            ExactGraphNumberParser.Format(snapshot.TotalValue),
            snapshot.ValueCurrency,
            snapshot.AvailableValue is { } available ? ExactGraphNumberParser.Format(available) : null,
            [
                .. snapshot.Coins.Select(coin => new AccountCoinBalanceResponse(
                    coin.Coin,
                    ExactGraphNumberParser.Format(coin.Balance),
                    coin.UsdValue is { } usd ? ExactGraphNumberParser.Format(usd) : null))
            ]);
    }
}