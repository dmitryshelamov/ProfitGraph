using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.GetVenueBotPositions;

/// <summary>
///     The position of every bot of a venue, whatever its state, from its last checkpoint (ADR 0031). A venue module uses it
///     to check that the account holds at least what its bots hold before it closes an order by itself.
/// </summary>
public sealed record GetVenueBotPositionsQuery(string Venue) : IQuery<IReadOnlyList<VenueBotPositionInternal>>;