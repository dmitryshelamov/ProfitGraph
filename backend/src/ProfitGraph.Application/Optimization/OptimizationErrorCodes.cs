namespace ProfitGraph.Application.Optimization;

public static class OptimizationErrorCodes
{
    /// <summary>
    ///     The search examined its candidate limit without finding enough trials that satisfy the template rules.
    /// </summary>
    public const string CandidateLimitExceeded = "candidate_limit_exceeded";

    /// <summary>
    ///     A trial threw an unexpected exception; the other trials continue.
    /// </summary>
    public const string TrialException = "trial_exception";
}