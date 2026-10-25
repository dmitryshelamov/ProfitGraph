namespace ProfitGraph.Api.Accounts.Contracts;

/// <summary>
///     Where an enrollment stands: the step it waits for and that step's fields, a secret shown only in this answer (such as a
///     recovery code to write down), a problem code, and the credential once stored.
/// </summary>
public sealed record CredentialEnrollmentResponse(
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
        return $"CredentialEnrollmentResponse {{ EnrollmentId = {EnrollmentId}, Step = {Step}, Redacted }}";
    }
}