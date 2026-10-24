using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.Ports;

/// <summary>
///     A venue's order execution for live bots (ADR 0016). It exists only in a Worker with the <c>live</c> role and only
///     when trading is enabled; without it a bot degrades with <c>NoGateway</c> and sends nothing. The gateway owns the
///     format of the client order identifiers the venue knows orders by.
/// </summary>
public interface IOrderGateway : IVenuePort
{
    /// <summary>
    ///     The deterministic venue identifier of an engine order of a bot.
    /// </summary>
    ClientOrderId ClientOrderIdFor(BotId botId, long engineOrderId);

    /// <summary>
    ///     The bot's order updates as the venue reports them, in an order the engine accepts.
    /// </summary>
    IAsyncEnumerable<ExternalOrderUpdate> Updates(BotId botId, CancellationToken cancellationToken);
}