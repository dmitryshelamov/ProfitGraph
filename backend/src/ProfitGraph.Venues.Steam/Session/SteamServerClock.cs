namespace ProfitGraph.Venues.Steam.Session;

/// <summary>
///     Steam's server time as an offset from ours, measured at the midpoint of a <c>QueryTime</c> request.
/// </summary>
internal sealed record SteamServerClock(TimeSpan Offset, DateTimeOffset MeasuredAt);