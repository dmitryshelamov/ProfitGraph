namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Учётное ядро одного прогона: портфель, ордера, замороженные параметры исполнения и правила инструмента.
///     Выставление резервирует средства и никогда не исполняет ордер; исполнение и отмена освобождают резерв.
///     Когда и по какому опорному ключу исполнять ордер, решает движок; ядро не читает часы и не знает о свечах.
/// </summary>
public sealed class TradingAccount : IGraphAccount
{
    private readonly OrderSizingService _sizing = new OrderSizingService();
    public Portfolio Portfolio { get; }
    public OrderBook Orders { get; }

    public ExecutionSettings Settings { get; }
    public InstrumentRules Rules { get; }

    public TradingAccount(ExecutionSettings settings, InstrumentRules rules)
    {
        Settings = settings;
        Rules = rules;
        Portfolio = new Portfolio(settings.StartingCapital);
        Orders = new OrderBook();
    }

    public AccountSnapshot Capture()
    {
        return AccountSnapshot.Capture(this);
    }

    /// <summary>
    ///     Принимает и резервирует ордер либо возвращает видимый отказ без изменения балансов.
    ///     Количество и лимитная цена округляются вниз; покупки увеличиваются до минимальной стоимости только при явной политике RaiseToExchangeMinimum.
    /// </summary>
    public OrderSubmission Submit(OrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OriginNodeId))
        {
            throw new TradingInvariantException("Order request must identify its originating node.");
        }

        if (request.Type == OrderType.Limit != request.LimitPrice.HasValue)
        {
            throw new TradingInvariantException("Only limit orders carry a limit price, and every limit order needs one.");
        }

        string? tag = string.IsNullOrWhiteSpace(request.Tag) ? null : request.Tag;
        OrderSizingResult sizing = _sizing.ResolveForSubmission(request, Rules, Settings, this);
        RejectionReason? reason = sizing.Reason;

        decimal? quantity = sizing.Quantity;
        decimal? price = sizing.Price;
        decimal reservation = sizing.Reservation;

        if (reason is not null)
        {
            return new OrderSubmission(Order: null, Reject(reason.Value, request.Side, request.Type, tag, request.OriginNodeId, orderId: null));
        }

        var order = new Order(
            Orders.NextId(),
            tag,
            request.Side,
            request.Type,
            quantity,
            price,
            request is { Side: OrderSide.Buy, Type: OrderType.Market } ? reservation : null,
            reservation,
            request.OriginNodeId,
            request.EligibleFromCandleIndex);

        if (order.Side == OrderSide.Buy)
        {
            Portfolio.ReserveQuote(reservation);
        }
        else
        {
            Portfolio.ReserveBase(reservation);
        }

        Orders.Add(order);

        return new OrderSubmission(order, Rejection: null);
    }

    /// <summary>
    ///     Отменяет активный ордер и освобождает резерв. Отсутствующий или терминальный ордер — успешный no-op: возвращается null.
    /// </summary>
    public Order? Cancel(long orderId)
    {
        return Cancel(Orders.Get(orderId));
    }

    public Order? CancelByTag(string tag)
    {
        return Cancel(Orders.GetActive(tag));
    }

    /// <summary>
    ///     Исполняет активный ордер целиком. Рыночный ордер исполняется по опорной цене с неблагоприятным проскальзыванием,
    ///     лимитный — по своей лимитной цене без улучшения (опорная цена для него не используется). Рыночный ордер,
    ///     который по фактической цене не проходит минимумы, отклоняется с освобождением резерва.
    /// </summary>
    public OrderExecution Execute(long orderId, decimal referencePrice)
    {
        Order order = Orders.Get(orderId) is { IsActive: true } active
            ? active
            : throw new TradingInvariantException($"Order {orderId} is not active.");

        if (order.Type == OrderType.Market && referencePrice <= 0m)
        {
            throw new TradingInvariantException("Market execution requires a positive reference price.");
        }

        decimal price = order.Type == OrderType.Limit ? order.LimitPrice!.Value : SlippageModel.Apply(referencePrice, order.Side, Settings.Slippage);
        decimal grossBase = order.Quantity ?? OrderNormalizer.NormalizeQuantity(order.QuoteBudget!.Value / price, Rules);

        if (order.Type == OrderType.Market && OrderNormalizer.CheckMinimums(grossBase, price, Rules) is { } reason)
        {
            Release(order, OrderStatus.Rejected);

            return new OrderExecution(Fill: null, Reject(reason, order.Side, order.Type, order.Tag, order.OriginNodeId, order.Id));
        }

        decimal principal = grossBase * price;
        decimal quoteFee = order.Side == OrderSide.Buy ? OrderSizingService.QuoteFee(principal, Rules, Settings, order.Type) : 0m;

        decimal rate = Settings.FeeRate(order.Type);

        Release(order, OrderStatus.Filled);

        if (order.Side == OrderSide.Buy)
        {
            decimal quoteSpent = order.Type == OrderType.Limit ? order.Reservation : grossBase * price;

            if (Rules.BuyFeeAsset == FeeAsset.Quote)
            {
                Portfolio.BuyWithQuoteFee(quoteSpent, grossBase, quoteFee, price);

                return new OrderExecution(
                    new Fill(
                        order.Id,
                        order.Tag,
                        order.Side,
                        order.Type,
                        price,
                        grossBase,
                        grossBase,
                        -(quoteSpent + quoteFee),
                        quoteFee,
                        FeeAsset.Quote,
                        RealizedPnl: 0m),
                    Rejection: null);
            }

            decimal fee = FeeCalculator.Calculate(grossBase, rate, Rules.BasePrecision);
            Portfolio.Buy(quoteSpent, grossBase, fee, price);

            return new OrderExecution(
                new Fill(order.Id, order.Tag, order.Side, order.Type, price, grossBase, grossBase - fee, -quoteSpent, fee, FeeAsset.Base, RealizedPnl: 0m),
                Rejection: null);
        }

        decimal grossQuote = grossBase * price;
        decimal sellFee = Rules.ExactQuoteFees ? grossQuote * rate : FeeCalculator.Calculate(grossQuote, rate, Rules.QuotePrecision);
        decimal realized = Portfolio.Sell(grossBase, grossQuote, sellFee);

        return new OrderExecution(
            new Fill(order.Id, order.Tag, order.Side, order.Type, price, grossBase, -grossBase, grossQuote - sellFee, sellFee, FeeAsset.Quote, realized),
            Rejection: null);
    }

    /// <summary>
    ///     Финальная очистка: отменяет все активные ордера без принудительной продажи; торговых событий здесь не возникает.
    /// </summary>
    public IReadOnlyList<Order> ReleaseAll()
    {
        Order[] active = Orders.Active.ToArray();

        foreach (Order order in active)
        {
            Release(order, OrderStatus.Cancelled);
        }

        return active;
    }

    private Order? Cancel(Order? order)
    {
        if (order is not { IsActive: true })
        {
            return null;
        }

        Release(order, OrderStatus.Cancelled);

        return order;
    }

    private void Release(Order order, OrderStatus status)
    {
        if (order.Side == OrderSide.Buy)
        {
            Portfolio.ReleaseQuote(order.Reservation);
        }
        else
        {
            Portfolio.ReleaseBase(order.Reservation);
        }

        order.Close(status);
        Orders.Remove(order);
    }

    internal static OrderRejection Reject(RejectionReason reason, OrderSide side, OrderType type, string? tag, string originNodeId, long? orderId)
    {
        string message = reason switch
        {
            RejectionReason.InvalidAmount => "Order amount must be positive after rounding to the asset precision.",
            RejectionReason.InvalidPrice => "Limit price must be positive after rounding down to the price tick.",
            RejectionReason.ZeroQuantity => "Quantity rounds down to zero at the quantity step.",
            RejectionReason.BelowMinQuantity => "Quantity is below the instrument minimum.",
            RejectionReason.BelowMinNotional => "Order value is below the instrument minimum notional.",
            RejectionReason.InsufficientQuote => "Available quote balance is insufficient.",
            RejectionReason.InsufficientBase => "Available base balance is insufficient.",
            RejectionReason.RiskLimit => "Order principal exceeds the allocation limit.",
            RejectionReason.TagActive => $"Tag '{tag}' already has an active order.",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, message: null)
        };

        return new OrderRejection(reason, message, side, type, tag, originNodeId, orderId);
    }
}