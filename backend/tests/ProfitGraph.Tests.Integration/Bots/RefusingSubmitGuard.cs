using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     A submit guard that refuses every buy and counts what it was asked.
/// </summary>
internal sealed class RefusingSubmitGuard : IOrderSubmitGuard
{
    public const string Reason = "The test guard refuses buys.";

    public int Asked { get; private set; }

    public Task<string?> Refusal(BotId botId, OrderSnapshot order, CancellationToken cancellationToken)
    {
        Asked++;

        return Task.FromResult(order.Side == OrderSide.Buy ? Reason : null);
    }
}