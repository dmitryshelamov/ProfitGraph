namespace ProfitGraph.Api.Accounts.Contracts;

/// <summary>
///     Answers the step an enrollment waits for with that step's fields, which are never stored or logged.
/// </summary>
public sealed record ContinueCredentialEnrollmentRequest(string? Venue, string? Step, IReadOnlyDictionary<string, string>? Fields)
{
    public override string ToString()
    {
        return $"ContinueCredentialEnrollmentRequest {{ Venue = {Venue}, Step = {Step}, Redacted }}";
    }
}