using ProfitGraph.Application.Accounts.Contracts;

namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     The exchange account a bot trades on, as the bot page summarizes it (ADR 0049); nothing in it is a secret or reads the
///     venue. It names the credential by its label and the key's last four characters, and the account by
///     <paramref name="AccountPublicId" /> (the credential's public identifier when it names the account, never a key) and the
///     cached <paramref name="Identity" />. The money is the latest snapshot with how reading it goes; the allocation is the
///     bot's own capital and that of the other bots of the credential that should run.
/// </summary>
public sealed record BotAccountInternal(
    Guid CredentialId,
    string Venue,
    string CredentialLabel,
    string ApiKeyLast4,
    string? AccountPublicId,
    AccountSnapshotInternal? Snapshot,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    string? FailureReason,
    DateTimeOffset? NextAttemptAt,
    AccountIdentityInternal? Identity,
    DateTimeOffset? IdentityReadAt,
    string? IdentityFailureReason,
    VenueAccountStatusInternal? VenueStatus,
    VenueAccountProtectionInternal Protection,
    decimal AllocationCap,
    IReadOnlyList<BotAllocationInternal> OtherRunningBots);