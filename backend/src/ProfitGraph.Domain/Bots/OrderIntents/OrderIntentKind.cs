namespace ProfitGraph.Domain.Bots.OrderIntents;

/// <summary>
///     What an order intent asks the venue to do.
/// </summary>
public enum OrderIntentKind
{
    Submit,
    Cancel
}