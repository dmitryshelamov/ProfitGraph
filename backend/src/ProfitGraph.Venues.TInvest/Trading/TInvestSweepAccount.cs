namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     When the post-session sweep next reads an account's orders, and how many sweeps of it in a row could not finish.
/// </summary>
internal sealed record TInvestSweepAccount(DateTimeOffset DueAt, int Failures);