using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     An occurrence with the inherited reached cursor and earliest order eligibility.
/// </summary>
public sealed record RoutedOccurrence(EventOccurrence Occurrence, ReachedMarketContext Market);