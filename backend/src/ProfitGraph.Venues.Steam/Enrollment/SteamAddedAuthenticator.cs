namespace ProfitGraph.Venues.Steam.Enrollment;

/// <summary>
///     The authenticator Steam created and waits to activate: its secrets, the recovery code for the owner to write down, and
///     how the activation is confirmed (3 is a code from email).
/// </summary>
internal sealed record SteamAddedAuthenticator(string SharedSecret, string IdentitySecret, string RecoveryCode, int ConfirmationType)
{
    public override string ToString()
    {
        return $"SteamAddedAuthenticator {{ ConfirmationType = {ConfirmationType}, Redacted }}";
    }
}