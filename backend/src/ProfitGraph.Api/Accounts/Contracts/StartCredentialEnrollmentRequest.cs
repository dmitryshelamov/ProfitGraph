namespace ProfitGraph.Api.Accounts.Contracts;

/// <summary>
///     Starts creating a credential with a venue that enrolls them in steps; the fields are the venue's own, such as an
///     account name and password, and are never stored or logged.
/// </summary>
public sealed record StartCredentialEnrollmentRequest(string? Venue, string? Label, IReadOnlyDictionary<string, string>? Fields)
{
    public override string ToString()
    {
        return $"StartCredentialEnrollmentRequest {{ Venue = {Venue}, Redacted }}";
    }
}