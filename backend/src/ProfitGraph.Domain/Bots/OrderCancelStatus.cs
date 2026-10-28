namespace ProfitGraph.Domain.Bots;

/// <summary>
///     Where an owner's request to cancel a bot's order is (ADR 0048).
/// </summary>
public enum OrderCancelStatus
{
    Pending,
    Requested,
    Rejected
}