namespace ProfitGraph.Domain.Bots;

/// <summary>
///     Where an order intent is on its way to the venue.
/// </summary>
public enum OrderIntentStatus
{
    Pending,
    Sending,
    Sent,
    Failed,
    Rejected
}