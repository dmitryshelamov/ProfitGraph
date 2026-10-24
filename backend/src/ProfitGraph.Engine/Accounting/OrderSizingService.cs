namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Shared pure submission sizing and reached-price preview. Market submission never assumes a future execution price.
/// </summary>
public sealed class OrderSizingService
{
    public OrderSizingResult Resolve(OrderRequest request, InstrumentRules rules, ExecutionSettings settings, AccountSnapshot account, decimal? referencePrice = null)
    {
        return Resolve(
            request.Side,
            request.Type,
            request.Amount,
            request.LimitPrice,
            rules,
            settings,
            account.QuoteAvailable - PendingQuoteFees(account.Orders, rules, settings),
            account.BaseAvailable,
            HasActiveTag(account.Orders, request.Tag),
            referencePrice,
            request.GetMinimumOrderPolicy());
    }

    internal OrderSizingResult ResolveForSubmission(OrderRequest request, InstrumentRules rules, ExecutionSettings settings, TradingAccount account)
    {
        return Resolve(
            request.Side,
            request.Type,
            request.Amount,
            request.LimitPrice,
            rules,
            settings,
            account.Portfolio.QuoteAvailable - (rules.BuyFeeAsset == FeeAsset.Quote
                ? PendingQuoteFees(account.Orders.Active.Select(OrderSnapshot.Capture), rules, settings)
                : 0m),
            account.Portfolio.BaseAvailable,
            !string.IsNullOrWhiteSpace(request.Tag) && account.Orders.GetActive(request.Tag) is not null,
            referencePrice: null,
            request.GetMinimumOrderPolicy());
    }

    internal OrderSizingResult ResolvePreview(
        OrderSide side,
        OrderType type,
        decimal amount,
        decimal? limitPrice,
        InstrumentRules rules,
        ExecutionSettings settings,
        AccountSnapshot account,
        decimal? referencePrice,
        MinimumOrderPolicy minimumOrderPolicy = MinimumOrderPolicy.Reject,
        decimal? quoteLimit = null)
    {
        decimal funding = quoteLimit.HasValue ? Math.Min(account.QuoteAvailable, quoteLimit.Value) : account.QuoteAvailable;

        return Resolve(
            side,
            type,
            amount,
            limitPrice,
            rules,
            settings,
            funding - PendingQuoteFees(account.Orders, rules, settings),
            account.BaseAvailable,
            tagActive: false,
            referencePrice,
            minimumOrderPolicy);
    }

    public static decimal ConvertIntentAmount(OrderSide side, bool sizeIsQuote, decimal size, decimal conversionPrice)
    {
        if (side == OrderSide.Buy == sizeIsQuote)
        {
            return size;
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(conversionPrice);

        return side == OrderSide.Buy ? size * conversionPrice : size / conversionPrice;
    }

    public static decimal QuoteFee(decimal principal, InstrumentRules rules, ExecutionSettings settings, OrderType type)
    {
        return rules.BuyFeeAsset != FeeAsset.Quote
            ? 0m
            : rules.ExactQuoteFees
                ? principal * settings.FeeRate(type)
                : FeeCalculator.Calculate(principal, settings.FeeRate(type), rules.QuotePrecision);
    }

    internal static decimal PendingQuoteFees(IEnumerable<OrderSnapshot> orders, InstrumentRules rules, ExecutionSettings settings)
    {
        if (rules.BuyFeeAsset != FeeAsset.Quote)
        {
            return 0m;
        }

        var fee = 0m;

        foreach (OrderSnapshot order in orders)
        {
            if (order.Side == OrderSide.Buy)
            {
                fee += QuoteFee(order.Reservation, rules, settings, order.Type);
            }
        }

        return fee;
    }

    private static bool HasActiveTag(IReadOnlyList<OrderSnapshot> orders, string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        for (var index = 0; index < orders.Count; index++)
        {
            if (orders[index].Tag == tag)
            {
                return true;
            }
        }

        return false;
    }

    private static OrderSizingResult Resolve(
        OrderSide side,
        OrderType type,
        decimal amount,
        decimal? limitPrice,
        InstrumentRules rules,
        ExecutionSettings settings,
        decimal quoteAvailable,
        decimal baseAvailable,
        bool tagActive,
        decimal? referencePrice,
        MinimumOrderPolicy minimumOrderPolicy)
    {
        decimal? quantity = null;
        decimal? price = null;
        var reservation = 0m;

        if (amount <= 0m)
        {
            return new OrderSizingResult(quantity, price, reservation, RejectionReason.InvalidAmount);
        }

        if (limitPrice is not null)
        {
            price = OrderNormalizer.NormalizePrice(limitPrice.Value, rules);

            if (price <= 0m)
            {
                return new OrderSizingResult(quantity, price, reservation, RejectionReason.InvalidPrice);
            }
        }

        if (tagActive)
        {
            return new OrderSizingResult(quantity, price, reservation, RejectionReason.TagActive);
        }

        bool raise = side == OrderSide.Buy && minimumOrderPolicy == MinimumOrderPolicy.RaiseToExchangeMinimum;

        // A raised buy is never cut to fit the remaining budget, even if lot rounding would make it fit.
        if (raise && amount > quoteAvailable)
        {
            return new OrderSizingResult(quantity, price, reservation, RejectionReason.InsufficientQuote);
        }

        if (side == OrderSide.Buy && type == OrderType.Market)
        {
            var quoteUnit = new decimal(lo: 1, mid: 0, hi: 0, isNegative: false, (byte)rules.QuotePrecision);
            reservation = DecimalRounding.FloorToStep(amount, quoteUnit);

            if (raise && reservation < rules.MinNotional)
            {
                reservation = decimal.Ceiling(rules.MinNotional / quoteUnit) * quoteUnit;
            }

            if (reservation <= 0m)
            {
                return new OrderSizingResult(quantity, price, reservation, RejectionReason.InvalidAmount);
            }

            if (reservation < rules.MinNotional)
            {
                return new OrderSizingResult(quantity, price, reservation, RejectionReason.BelowMinNotional);
            }

            if (reservation > quoteAvailable)
            {
                return new OrderSizingResult(quantity, price, reservation, RejectionReason.InsufficientQuote);
            }

            if (referencePrice.HasValue)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(referencePrice.Value);
                decimal reached = SlippageModel.Apply(referencePrice.Value, side, settings.Slippage);
                quantity = OrderNormalizer.NormalizeQuantity(reservation / reached, rules);

                if (OrderNormalizer.CheckMinimums(quantity.Value, reached, rules) is { } minimum)
                {
                    return new OrderSizingResult(quantity, price, reservation, minimum);
                }
            }

            return new OrderSizingResult(quantity, price, reservation, Reason: null);
        }

        quantity = OrderNormalizer.NormalizeQuantity(side == OrderSide.Buy ? amount / price!.Value : amount, rules);

        if (raise && price.HasValue && quantity.Value * price.Value < rules.MinNotional)
        {
            quantity = decimal.Ceiling(rules.MinNotional / price.Value / rules.QuantityStep) * rules.QuantityStep;
        }

        decimal? minimumPrice = price;

        if (type == OrderType.Market && referencePrice.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(referencePrice.Value);
            minimumPrice = SlippageModel.Apply(referencePrice.Value, side, settings.Slippage);
        }

        if (OrderNormalizer.CheckMinimums(quantity.Value, minimumPrice, rules) is { } failure)
        {
            return new OrderSizingResult(quantity, price, reservation, failure);
        }

        if (side == OrderSide.Sell)
        {
            reservation = quantity.Value;

            return new OrderSizingResult(quantity, price, reservation, reservation > baseAvailable ? RejectionReason.InsufficientBase : null);
        }

        reservation = quantity.Value * price!.Value;

        decimal quoteFee = QuoteFee(reservation, rules, settings, type);

        return new OrderSizingResult(quantity, price, reservation, reservation + quoteFee > quoteAvailable ? RejectionReason.InsufficientQuote : null)
        {
            QuoteFee = quoteFee
        };
    }
}