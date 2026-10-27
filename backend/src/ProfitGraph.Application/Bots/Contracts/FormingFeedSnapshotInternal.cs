using ProfitGraph.Application.Bots.JoinBotFormingFeed;

namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     What a client joining a forming-candle feed learns at once: the feed it follows, its state and the latest candle the
///     feed holds, so the client need not wait for the next update.
/// </summary>
public sealed record FormingFeedSnapshotInternal(FormingFeedKeyInternal Key, FormingFeedStatus Status, FormingCandleInternal? Candle);