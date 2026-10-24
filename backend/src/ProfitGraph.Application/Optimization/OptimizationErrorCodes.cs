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

    /// <summary>
    ///     A stored trial does not match the candidate the frozen search enumerates at its index.
    /// </summary>
    public const string PersistedTrialMismatch = "persisted_trial_mismatch";

    /// <summary>
    ///     History prepared again on resume no longer matches the history the optimization attached.
    /// </summary>
    public const string HistoryMismatch = "optimization_history_mismatch";
}