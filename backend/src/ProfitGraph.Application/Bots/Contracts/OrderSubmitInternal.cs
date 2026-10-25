using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     A submit for the venue: the engine's order, the identifier the venue knows it by and whether a place request may
///     already have reached the venue.
/// </summary>
public sealed record OrderSubmitInternal(BotId BotId, ClientOrderId ClientOrderId, OrderSnapshot Order, bool MustLookUpBeforeSubmit);