namespace ProfitGraph.Application.Accounts.Contracts;

/// <summary>
///     Where an enrollment stands: the step it waits for and the fields that step takes, a secret shown once (such as a
///     recovery code, which nothing stores), a problem code when the last step was refused, and, after the last step, the
///     credential to store.
/// </summary>
public sealed record CredentialEnrollmentStepInternal(
    string Step,
    IReadOnlyList<string> Fields,
    string? OneTimeSecret = null,
    string? Problem = null,
    EnrolledCredentialInternal? Completed = null)
{
    /// <summary>
    ///     The step of an enrollment whose credential is stored.
    /// </summary>
    public const string CompletedStep = "completed";

    /// <summary>
    ///     The step of an enrollment that cannot go on; its problem says why.
    /// </summary>
    public const string StoppedStep = "stopped";

    public override string ToString()
    {
        return $"CredentialEnrollmentStepInternal {{ Step = {Step}, Problem = {Problem}, Redacted }}";
    }
}