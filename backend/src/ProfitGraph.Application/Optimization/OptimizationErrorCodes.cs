namespace ProfitGraph.Application.Optimization;

public static class OptimizationErrorCodes
{
    public const string NotFound = "optimization_not_found";
    public const string InvalidOptimization = "invalid_optimization";

    /// <summary>
    ///     The strategy version no longer compiles with the frozen ranges to the frozen fingerprint.
    /// </summary>
    public const string CompilationChanged = "optimization_compilation_changed";

    /// <summary>
    ///     The search examined its candidate limit without finding enough trials that satisfy the template rules.
    /// </summary>
    public const string CandidateLimitExceeded = "candidate_limit_exceeded";

    /// <summary>
    ///     A trial threw an unexpected exception; the other trials continue.
    /// </summary>
    public const string TrialException = "trial_exception";
}