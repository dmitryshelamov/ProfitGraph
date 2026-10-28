namespace ProfitGraph.Application.Bots.DispatchOrderIntent;

/// <summary>
///     Section <c>Bots:Dispatch</c>: how many due intents one dispatch looks at and when a deferred submit is asked about
///     again (ADR 0024).
/// </summary>
public sealed class OrderDispatchOptions
{
    public const string SectionName = "Bots:Dispatch";

    /// <summary>
    ///     How many due intents one dispatch looks at.
    /// </summary>
    public int CandidateLimit { get; init; } = 20;

    /// <summary>
    ///     The shortest wait before a deferred submit is asked about again, so a venue cannot make the dispatcher spin.
    /// </summary>
    public TimeSpan DeferralFloor { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     The longest wait before a deferred submit is asked about again.
    /// </summary>
    public TimeSpan DeferralCeiling { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    ///     The wait before an unreadable readiness answer is asked again.
    /// </summary>
    public TimeSpan UnknownReadinessRetry { get; init; } = TimeSpan.FromSeconds(30);

    public bool IsValid()
    {
        return CandidateLimit > 0 && DeferralFloor > TimeSpan.Zero && DeferralCeiling >= DeferralFloor && UnknownReadinessRetry > TimeSpan.Zero;
    }
}