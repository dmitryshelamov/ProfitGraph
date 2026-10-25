using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Venues.Steam.Enrollment;

/// <summary>
///     A pending enrollment as stored: its owner, its step, its encrypted state and when it expires.
/// </summary>
internal sealed record SteamPendingEnrollment(
    Guid Id,
    Guid OwnerId,
    string Step,
    EncryptedSecret Payload,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public override string ToString()
    {
        return $"SteamPendingEnrollment {{ Id = {Id}, Step = {Step}, Redacted }}";
    }
}