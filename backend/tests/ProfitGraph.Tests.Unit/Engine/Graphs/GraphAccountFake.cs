using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

internal sealed class GraphAccountFake : IGraphAccount
{
    private readonly OrderSubmission _submission;
    private Order? _order;
    public OrderRequest? SubmittedRequest { get; private set; }
    public string? CancelledTag { get; private set; }

    public ExecutionSettings Settings { get; } = new ExecutionSettings(startingCapital: 250m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m);
    public InstrumentRules Rules { get; } = new InstrumentRules(quantityStep: 1m, priceTick: 1m, minQuantity: 1m, minNotional: 1m, basePrecision: 0, quotePrecision: 0);

    public GraphAccountFake(OrderSubmission submission)
    {
        _submission = submission;
    }

    public AccountSnapshot Capture()
    {
        decimal reserved = _order?.Reservation ?? 0m;

        return AccountSnapshot.Restore(
            Settings.StartingCapital,
            Settings.StartingCapital,
            reserved,
            baseTotal: 0m,
            baseReserved: 0m,
            costBasis: 0m,
            realizedPnl: 0m,
            feesBase: 0m,
            feesQuote: 0m,
            lastBuyPrice: null,
            _order is null ? Array.Empty<OrderSnapshot>() : new[] { OrderSnapshot.Capture(_order) });
    }

    public OrderSubmission Submit(OrderRequest request)
    {
        SubmittedRequest = request;
        _order = _submission.Order;

        return _submission;
    }

    public Order? Cancel(long orderId)
    {
        throw new NotSupportedException();
    }

    public Order? CancelByTag(string tag)
    {
        CancelledTag = tag;

        if (_order is not { IsActive: true } order || order.Tag != tag)
        {
            return null;
        }

        order.Close(OrderStatus.Cancelled);
        _order = null;

        return order;
    }
}