using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.JoinBotFormingFeed;

/// <summary>
///     Joins a client connection to the forming-candle feed of the owner's bot (ADR 0026), replacing the feed the connection
///     followed before. Another user's bot is reported as missing. The feed keeps no state in the database.
/// </summary>
public sealed record JoinBotFormingFeedCommand(Guid OwnerId, Guid BotId, string ConnectionId) : ICommand<FormingFeedSnapshotInternal>, INonTransactionalCommand;