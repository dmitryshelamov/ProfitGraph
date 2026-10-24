using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.MarkFeedStale;

/// <summary>
///     A venue's live feed of one series stopped delivering closed candles, or delivers them again.
/// </summary>
public sealed record MarkFeedStaleCommand(string Venue, string Symbol, string Timeframe, bool Stale) : ICommand;