using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     An invented brokerage account behind the mock Invest API: its accounts, the last price, the trading status, the price band, the trading schedule,
///     <c>PostOrder</c>,
///     <c>GetOrderState</c> and <c>CancelOrder</c> by request ID, <c>GetOrders</c> and an <c>OrderStateStream</c> that confirms
///     its subscription and then signals every change. Orders are kept by request ID like T-Invest's idempotency key, so a
///     repeated send would not place a second order, and the test can see how many were sent.
/// </summary>
internal sealed class MockTInvestOrders
{
    private readonly Dictionary<string, OrderState> _orders = new Dictionary<string, OrderState>(StringComparer.Ordinal);
    private readonly List<Channel<OrderStateStreamResponse>> _subscribers = [];
    private long _exchangeIds = 7_000_000;

    public List<PostOrderRequest> Posted { get; } = [];

    public List<CancelOrderRequest> Cancelled { get; } = [];

    public string AccountId { get; }

    public decimal LastPrice { get; set; } = 100m;

    public AccessLevel Access { get; set; } = AccessLevel.AccountAccessLevelFullAccess;

    /// <summary>
    ///     How the next <c>PostOrder</c> fails: the order is not placed, unless <see cref="PlaceBeforeFailing" /> says the
    ///     broker placed it and only the answer was lost.
    /// </summary>
    public RpcException? NextPostFailure { get; set; }

    public bool PlaceBeforeFailing { get; set; }

    public RpcException? NextCancelFailure { get; set; }

    public bool TradingOpen { get; set; } = true;

    /// <summary>
    ///     Lots of every new buy that execute at its price as soon as it is placed.
    /// </summary>
    public long BuyLotsFilledOnPost { get; set; }

    /// <summary>
    ///     The share's current price limits, or null when the order book reports none.
    /// </summary>
    public (decimal Low, decimal High)? Band { get; set; } = (50m, 150m);

    public int BookReads { get; private set; }

    /// <summary>
    ///     The calendar's trading days with their interval tokens, as <c>TradingSchedules</c> returns them.
    /// </summary>
    public List<(DateOnly Date, List<(string Type, DateTimeOffset From, DateTimeOffset To)> Intervals)> Schedule { get; } = [];

    public MockTInvestOrders(MockTInvestApi api, string accountId)
    {
        AccountId = accountId;
        api.On<GetAccountsRequest, GetAccountsResponse>(UsersService.Descriptor, "GetAccounts", _ => Accounts());
        api.On<GetLastPricesRequest, GetLastPricesResponse>(MarketDataService.Descriptor, "GetLastPrices", LastPrices);
        api.On<PostOrderRequest, PostOrderResponse>(OrdersService.Descriptor, "PostOrder", Post);
        api.On<GetOrderStateRequest, OrderState>(OrdersService.Descriptor, "GetOrderState", State);
        api.On<GetOrdersRequest, GetOrdersResponse>(OrdersService.Descriptor, "GetOrders", Active);
        api.On<CancelOrderRequest, CancelOrderResponse>(OrdersService.Descriptor, "CancelOrder", Cancel);
        api.On<GetTradingStatusRequest, GetTradingStatusResponse>(MarketDataService.Descriptor, "GetTradingStatus", Status);
        api.On<GetOrderBookRequest, GetOrderBookResponse>(MarketDataService.Descriptor, "GetOrderBook", Book);
        api.On<TradingSchedulesRequest, TradingSchedulesResponse>(InstrumentsService.Descriptor, "TradingSchedules", Schedules);
        api.OnStream<OrderStateStreamRequest, OrderStateStreamResponse>(OrdersStreamService.Descriptor, "OrderStateStream", Stream);
    }

    public OrderState Order(string requestId)
    {
        lock (_orders)
        {
            return _orders[requestId];
        }
    }

    /// <summary>
    ///     Executes lots of the order at a price per share and signals the change.
    /// </summary>
    public void Fill(string requestId, long lots, decimal price, decimal commission)
    {
        OrderState order;

        lock (_orders)
        {
            order = _orders[requestId];
            order.Stages.Add(new OrderStage { Quantity = lots, Price = Money(price), TradeId = Guid.NewGuid().ToString("N") });
            order.LotsExecuted += lots;
            order.ExecutedCommission = Money(commission);
            order.ExecutionReportStatus = order.LotsExecuted == order.LotsRequested
                ? OrderExecutionReportStatus.ExecutionReportStatusFill
                : OrderExecutionReportStatus.ExecutionReportStatusPartiallyfill;
        }

        Signal(order);
    }

    /// <summary>
    ///     T-Invest reports the order's commission later, without another execution or stream message.
    /// </summary>
    public void Commission(string requestId, decimal commission)
    {
        lock (_orders)
        {
            _orders[requestId].ExecutedCommission = Money(commission);
        }
    }

    /// <summary>
    ///     The exchange cancels the order by itself, as at the end of a DAY order's trading day.
    /// </summary>
    public void Expire(string requestId)
    {
        OrderState order;

        lock (_orders)
        {
            order = _orders[requestId];
            order.ExecutionReportStatus = OrderExecutionReportStatus.ExecutionReportStatusCancelled;
        }

        Signal(order);
    }

    /// <summary>
    ///     An order placed by someone else on the account, outside ProfitGraph.
    /// </summary>
    public void AddForeign(string instrumentUid)
    {
        var order = new OrderState
        {
            OrderId = "foreign-" + Interlocked.Increment(ref _exchangeIds),
            OrderRequestId = Guid.NewGuid().ToString("D"),
            InstrumentUid = instrumentUid,
            LotsRequested = 1,
            ExecutionReportStatus = OrderExecutionReportStatus.ExecutionReportStatusNew
        };

        lock (_orders)
        {
            _orders[order.OrderRequestId] = order;
        }

        Signal(order);
    }

    private GetAccountsResponse Accounts()
    {
        return new GetAccountsResponse
        {
            Accounts =
            {
                new Account
                {
                    Id = AccountId,
                    Name = "Invented account",
                    Type = AccountType.Tinkoff,
                    Status = AccountStatus.Open,
                    AccessLevel = Access
                }
            }
        };
    }

    private GetLastPricesResponse LastPrices(GetLastPricesRequest request)
    {
        return new GetLastPricesResponse
        {
            LastPrices = { request.InstrumentId.Select(uid => new LastPrice { InstrumentUid = uid, Price = Quotation(LastPrice) }) }
        };
    }

    private GetTradingStatusResponse Status(GetTradingStatusRequest request)
    {
        return new GetTradingStatusResponse
        {
            InstrumentUid = request.InstrumentId,
            ApiTradeAvailableFlag = TradingOpen,
            LimitOrderAvailableFlag = TradingOpen,
            MarketOrderAvailableFlag = TradingOpen,
            TradingStatus = TradingOpen ? SecurityTradingStatus.NormalTrading : SecurityTradingStatus.NotAvailableForTrading
        };
    }

    private TradingSchedulesResponse Schedules(TradingSchedulesRequest request)
    {
        var schedule = new TradingSchedule { Exchange = request.Exchange.ToUpperInvariant() };

        foreach ((DateOnly date, List<(string Type, DateTimeOffset From, DateTimeOffset To)> intervals) in Schedule)
        {
            var day = new TradingDay { Date = Timestamp.FromDateTime(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), IsTradingDay = intervals.Count > 0 };
            day.Intervals.AddRange(
                intervals.Select(interval => new TradingInterval
                {
                    Type = interval.Type,
                    Interval = new TradingInterval.Types.TimeInterval
                    {
                        StartTs = Timestamp.FromDateTimeOffset(interval.From),
                        EndTs = Timestamp.FromDateTimeOffset(interval.To)
                    }
                }));
            schedule.Days.Add(day);
        }

        return new TradingSchedulesResponse { Exchanges = { schedule } };
    }

    private GetOrderBookResponse Book(GetOrderBookRequest request)
    {
        BookReads++;

        return Band is { } band
            ? new GetOrderBookResponse { InstrumentUid = request.InstrumentId, Depth = 1, LimitDown = Quotation(band.Low), LimitUp = Quotation(band.High) }
            : new GetOrderBookResponse { InstrumentUid = request.InstrumentId, Depth = 1 };
    }

    private PostOrderResponse Post(PostOrderRequest request)
    {
        OrderState order;

        lock (_orders)
        {
            Posted.Add(request);
            RpcException? failure = NextPostFailure;
            NextPostFailure = null;

            if (failure is not null && !PlaceBeforeFailing)
            {
                throw failure;
            }

            if (!_orders.TryGetValue(request.OrderId, out order!))
            {
                order = new OrderState
                {
                    OrderId = Interlocked.Increment(ref _exchangeIds).ToString(CultureInfo.InvariantCulture),
                    OrderRequestId = request.OrderId,
                    InstrumentUid = request.InstrumentId,
                    Direction = request.Direction,
                    OrderType = request.OrderType,
                    LotsRequested = request.Quantity,
                    ExecutionReportStatus = OrderExecutionReportStatus.ExecutionReportStatusNew
                };
                _orders[request.OrderId] = order;

                if (request.Direction == OrderDirection.Buy && BuyLotsFilledOnPost > 0 && request.Price is { } price)
                {
                    long lots = Math.Min(BuyLotsFilledOnPost, request.Quantity);
                    order.Stages.Add(
                        new OrderStage { Quantity = lots, Price = Money(price.Units + price.Nano / 1_000_000_000m), TradeId = Guid.NewGuid().ToString("N") });
                    order.LotsExecuted = lots;
                    order.ExecutedCommission = Money(0m);
                    order.ExecutionReportStatus = lots == request.Quantity
                        ? OrderExecutionReportStatus.ExecutionReportStatusFill
                        : OrderExecutionReportStatus.ExecutionReportStatusPartiallyfill;
                }
            }

            if (failure is not null)
            {
                throw failure;
            }
        }

        Signal(order);

        return new PostOrderResponse
        {
            OrderId = order.OrderId,
            OrderRequestId = order.OrderRequestId,
            ExecutionReportStatus = order.ExecutionReportStatus,
            LotsRequested = order.LotsRequested,
            LotsExecuted = order.LotsExecuted
        };
    }

    private OrderState State(GetOrderStateRequest request)
    {
        lock (_orders)
        {
            return request.OrderIdType == OrderIdType.Request && _orders.TryGetValue(request.OrderId, out OrderState? order)
                ? order.Clone()
                : throw MockTInvestApi.Refusal(StatusCode.NotFound, "50005");
        }
    }

    private GetOrdersResponse Active(GetOrdersRequest request)
    {
        lock (_orders)
        {
            return new GetOrdersResponse
            {
                Orders =
                {
                    _orders.Values.Where(order => order.ExecutionReportStatus is OrderExecutionReportStatus.ExecutionReportStatusNew
                            or OrderExecutionReportStatus.ExecutionReportStatusPartiallyfill)
                        .Select(order => order.Clone())
                }
            };
        }
    }

    private CancelOrderResponse Cancel(CancelOrderRequest request)
    {
        OrderState order;

        lock (_orders)
        {
            Cancelled.Add(request);
            RpcException? failure = NextCancelFailure;
            NextCancelFailure = null;

            if (failure is not null)
            {
                throw failure;
            }

            if (request.OrderIdType != OrderIdType.Request || !_orders.TryGetValue(request.OrderId, out order!) ||
                order.ExecutionReportStatus is not (OrderExecutionReportStatus.ExecutionReportStatusNew or OrderExecutionReportStatus.ExecutionReportStatusPartiallyfill))
            {
                throw MockTInvestApi.Refusal(StatusCode.NotFound, "30059");
            }

            order.ExecutionReportStatus = OrderExecutionReportStatus.ExecutionReportStatusCancelled;
        }

        Signal(order);

        return new CancelOrderResponse { Time = Timestamp.FromDateTime(DateTime.UtcNow) };
    }

    private async IAsyncEnumerable<OrderStateStreamResponse> Stream(OrderStateStreamRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<OrderStateStreamResponse>();

        lock (_subscribers)
        {
            _subscribers.Add(channel);
        }

        yield return new OrderStateStreamResponse { Subscription = new SubscriptionResponse { Status = ResultSubscriptionStatus.Ok } };

        await foreach (OrderStateStreamResponse message in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return message;
        }
    }

    private void Signal(OrderState order)
    {
        var message = new OrderStateStreamResponse
        {
            OrderState = new OrderStateStreamResponse.Types.OrderState
            {
                OrderId = order.OrderId,
                OrderRequestId = order.OrderRequestId,
                AccountId = AccountId,
                InstrumentUid = order.InstrumentUid,
                ExecutionReportStatus = order.ExecutionReportStatus,
                LotsRequested = order.LotsRequested,
                LotsExecuted = order.LotsExecuted
            }
        };

        lock (_subscribers)
        {
            foreach (Channel<OrderStateStreamResponse> subscriber in _subscribers)
            {
                subscriber.Writer.TryWrite(message);
            }
        }
    }

    private static MoneyValue Money(decimal value)
    {
        var units = decimal.ToInt64(decimal.Truncate(value));

        return new MoneyValue { Currency = "rub", Units = units, Nano = decimal.ToInt32((value - units) * 1_000_000_000m) };
    }

    private static Quotation Quotation(decimal value)
    {
        var units = decimal.ToInt64(decimal.Truncate(value));

        return new Quotation { Units = units, Nano = decimal.ToInt32((value - units) * 1_000_000_000m) };
    }
}