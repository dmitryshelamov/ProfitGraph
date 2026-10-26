namespace ProfitGraph.Venues.Steam.Enrollment;

/// <summary>
///     What a pending enrollment remembers between steps, stored encrypted: the label, the started sign-in, the new device
///     ID, and once known the account's tokens and the new authenticator's secrets. The password and the recovery code are
///     never part of it.
/// </summary>
internal sealed record SteamEnrollmentState(
    string Label,
    string SteamId,
    string ClientId,
    string RequestId,
    string DeviceId,
    string? RefreshToken = null,
    string? AccessToken = null,
    string? SharedSecret = null,
    string? IdentitySecret = null,
    string? AccountName = null)
{
    public override string ToString()
    {
        return "SteamEnrollmentState { Redacted }";
    }
}