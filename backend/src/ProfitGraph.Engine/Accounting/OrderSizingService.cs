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
            settings.IsMargin ? request.GetReferencePrice() : null,
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
        // A margin run's opening order reserves its fee with its margin (ADR 0061).
        if (rules.BuyFeeAsset != FeeAsset.Quote || settings.IsMargin)
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

            // A seller-paid fee makes some prices unreachable; a sell is raised to the next reachable one, never lowered.
            if (side == OrderSide.Sell && settings.SellerFees is { } sellerFees && price > 0m)
            {
                price = sellerFees.RaiseToRepresentable(price.Value, rules.QuotePrecision);
            }

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

        if (settings.IsMargin)
        {
            return ResolveMargin(side, type, amount, price, rules, settings, quoteAvailable, baseAvailable, referencePrice, raise);
        }

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

            // A quote-paid fee comes on top of the budget; the fill spends at most the budget plus its fee.
            decimal marketQuoteFee = QuoteFee(reservation, rules, settings, type);

            if (reservation + marketQuoteFee > quoteAvailable)
            {
                return new OrderSizingResult(quantity, price, reservation, RejectionReason.InsufficientQuote);
            }

            // The quote budget is the order value and already meets the minimum notional; the reached price checks only the quantity.
            if (referencePrice.HasValue)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(referencePrice.Value);
                decimal reached = SlippageModel.Apply(referencePrice.Value, side, settings.Slippage);
                quantity = OrderNormalizer.NormalizeQuantity(reservation / reached, rules);

                if (OrderNormalizer.CheckMinimums(quantity.Value, price: null, rules) is { } minimum)
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

    /// <summary>
    ///     Sizes an order of a margin run (ADR 0061). An exit (a long's sale, a short's cover) gives a base quantity and reserves the
    ///     position it closes. An entry reserves its notional divided by the leverage plus its quote fee: a long's buy spends a quote
    ///     amount as on spot, a short's sale gives a base quantity, and a market sale is valued at the slipped reference price.
    /// </summary>
    private static OrderSizingResult ResolveMargin(
        OrderSide side,
        OrderType type,
        decimal amount,
        decimal? price,
        InstrumentRules rules,
        ExecutionSettings settings,
        decimal quoteAvailable,
        decimal baseAvailable,
        decimal? referencePrice,
        bool raise)
    {
        decimal leverage = settings.Leverage!.Value;
        decimal? reached = referencePrice is { } reference && reference > 0m ? SlippageModel.Apply(reference, side, settings.Slippage) : null;
        decimal? quantity;

        if (side != settings.OpeningSide)
        {
            quantity = OrderNormalizer.NormalizeQuantity(amount, rules);

            if (OrderNormalizer.CheckMinimums(quantity.Value, price ?? reached, rules) is { } exitFailure)
            {
                return new OrderSizingResult(quantity, price, Reservation: 0m, exitFailure);
            }

            return new OrderSizingResult(quantity, price, quantity.Value, quantity.Value > baseAvailable ? RejectionReason.InsufficientBase : null);
        }

        if (side == OrderSide.Buy && type == OrderType.Market)
        {
            var quoteUnit = new decimal(lo: 1, mid: 0, hi: 0, isNegative: false, (byte)rules.QuotePrecision);
            decimal budget = DecimalRounding.FloorToStep(amount, quoteUnit);

            if (raise && budget < rules.MinNotional)
            {
                budget = decimal.Ceiling(rules.MinNotional / quoteUnit) * quoteUnit;
            }

            if (budget <= 0m)
            {
                return new OrderSizingResult(Quantity: null, price, Reservation: 0m, RejectionReason.InvalidAmount);
            }

            if (budget < rules.MinNotional)
            {
                return new OrderSizingResult(Quantity: null, price, Reservation: 0m, RejectionReason.BelowMinNotional);
            }

            quantity = reached is { } buyPrice ? OrderNormalizer.NormalizeQuantity(budget / buyPrice, rules) : null;

            if (quantity is { } bought && OrderNormalizer.CheckMinimums(bought, price: null, rules) is { } buyFailure)
            {
                return new OrderSizingResult(quantity, price, Reservation: 0m, buyFailure);
            }

            decimal budgetFee = QuoteFee(budget, rules, settings, type);
            decimal budgetMargin = budget / leverage + budgetFee;

            return new OrderSizingResult(quantity, price, budgetMargin, budgetMargin > quoteAvailable ? RejectionReason.InsufficientQuote : null)
            {
                QuoteFee = budgetFee,
                QuoteBudget = budget
            };
        }

        decimal? valuation = price ?? reached;

        if (valuation is not { } entryPrice)
        {
            return new OrderSizingResult(Quantity: null, price, Reservation: 0m, RejectionReason.InvalidPrice);
        }

        quantity = OrderNormalizer.NormalizeQuantity(side == OrderSide.Buy ? amount / entryPrice : amount, rules);

        if (raise && quantity.Value * entryPrice < rules.MinNotional)
        {
            quantity = decimal.Ceiling(rules.MinNotional / entryPrice / rules.QuantityStep) * rules.QuantityStep;
        }

        if (OrderNormalizer.CheckMinimums(quantity.Value, entryPrice, rules) is { } failure)
        {
            return new OrderSizingResult(quantity, price, Reservation: 0m, failure);
        }

        decimal notional = quantity.Value * entryPrice;
        decimal fee = QuoteFee(notional, rules, settings, type);
        decimal margin = notional / leverage + fee;

        return new OrderSizingResult(quantity, price, margin, margin > quoteAvailable ? RejectionReason.InsufficientQuote : null) { QuoteFee = fee };
    }
}
