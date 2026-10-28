using ProfitGraph.Api.Accounts.Contracts;

namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     The exchange account the bot trades on (ADR 0049): the key by its label and last four characters, the account by its
///     public identifier and the identity the venue last reported, the latest account value with how reading it goes, the
///     venue's status and protection of the account, and the capital of this bot and the other running bots of the key.
///     Decimals are exact strings; nothing in it is a secret.
/// </summary>
public sealed record BotAccountResponse(
    Guid CredentialId,
    string Venue,
    string CredentialLabel,
    string ApiKeyLast4,
    string? AccountPublicId,
    BotAccountIdentityResponse? Identity,
    string? IdentityFailureReason,
    AccountSnapshotResponse? Snapshot,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    string? FailureReason,
    DateTimeOffset? NextAttemptAt,
    AccountStatusResponse? VenueStatus,
    AccountProtectionResponse Protection,
    string AllocationCap,
    IReadOnlyList<BotAllocationResponse> OtherRunningBots);