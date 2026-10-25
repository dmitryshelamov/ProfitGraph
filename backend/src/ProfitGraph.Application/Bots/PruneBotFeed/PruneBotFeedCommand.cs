using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.PruneBotFeed;

/// <summary>
///     Removes feed entries older than the retention; returns how many.
/// </summary>
public sealed record PruneBotFeedCommand : ICommand<int>;