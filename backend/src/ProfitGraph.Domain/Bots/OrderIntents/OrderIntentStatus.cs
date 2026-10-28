namespace ProfitGraph.Domain.Bots.OrderIntents;

/// <summary>
///     Where an order intent is on its way to the venue.
/// </summary>
public enum OrderIntentStatus
{
    Pending,

    /// <summary>
    ///     Accepted by the bot but held in the core until a condition holds (ADR 0024); it never left the process.
    /// </summary>
    Deferred,
    Sending,
    Sent,
    Failed,
    Rejected
}