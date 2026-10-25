namespace ProfitGraph.Application.Accounts.Contracts;

/// <summary>
///     An enrollment as its owner sees it: the step it waits for and its fields, a secret shown once, a problem code, and the
///     stored credential once complete.
/// </summary>
public sealed record CredentialEnrollmentInternal(
    Guid EnrollmentId,
    string Venue,
    string Step,
    IReadOnlyList<string> Fields,
    string? OneTimeSecret,
    string? Problem,
    Guid? CredentialId)
{
    public override string ToString()
    {
        return $"CredentialEnrollmentInternal {{ EnrollmentId = {EnrollmentId}, Step = {Step}, Problem = {Problem}, Redacted }}";
    }
}