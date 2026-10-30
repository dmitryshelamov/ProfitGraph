namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     Where a followed card stands after the minutes seen so far (ADR 0071, sections 2 and 7): how many minutes its entry has waited,
///     when it entered and how many minutes it has been held since, including the filling minute, and its outcome once it has one.
/// </summary>
public sealed record ScanCardProgress(int Waited, DateTimeOffset? EnteredAt, int Held, ScanCardOutcome? Outcome)
{
    public static ScanCardProgress Start { get; } = new ScanCardProgress(Waited: 0, EnteredAt: null, Held: 0, Outcome: null);

    public bool Done => Outcome is not null;
}