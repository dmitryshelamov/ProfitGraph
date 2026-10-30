using System.Collections.Immutable;

using ProfitGraph.Engine.Dispatch;

namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     In-memory account for externally confirmed orders. It performs no exchange I/O or persistence.
/// </summary>
public sealed class ExternallyExecutedAccount : IExternallyExecutedAccount
{
    private readonly TradingAccount _account;
    private readonly Dictionary<long, ExternalOrderProgress> _progress = new Dictionary<long, ExternalOrderProgress>();
    private readonly Dictionary<long, ExternalFeeReceiptCheckpoint> _feeReceipts = new Dictionary<long, ExternalFeeReceiptCheckpoint>();

    /// <summary>
    ///     Whether the owner is closing the position (ADR 0064): only the close's own order is accepted. The dispatcher sets it
    ///     from its close state, which the checkpoint keeps.
    /// </summary>
    internal bool ClosingPosition { get; set; }

    public ExecutionSettings Settings => _account.Settings;
    public InstrumentRules Rules => _account.Rules;

    public ExternallyExecutedAccount(ExecutionSettings settings, InstrumentRules rules)
    {
        _account = new TradingAccount(settings, rules);

        if (rules.BuyFeeAsset == FeeAsset.Quote && !settings.IsMargin)
        {
            _account.Portfolio.ConfigureExternalQuoteFees();
        }
    }

    public AccountSnapshot Capture()
    {
        Portfolio portfolio = _account.Portfolio;

        return AccountSnapshot.RestoreForExternal(
            portfolio.StartingCapital,
            portfolio.QuoteTotal,
            portfolio.QuoteReserved,
            portfolio.BaseTotal,
            portfolio.BaseReserved,
            portfolio.CostBasis,
            portfolio.RealizedPnl,
            portfolio.FeesBase,
            portfolio.FeesQuote,
            portfolio.LastBuyPrice,
            _account.Orders.Active.Select(OrderSnapshot.Capture),
            portfolio.PendingFeeReserve,
            Rules.BuyFeeAsset == FeeAsset.Quote,
            portfolio.EntryNotional,
            portfolio.MarginRelease,
            Settings.Direction == PositionDirection.Short);
    }

    public OrderSubmission Submit(OrderRequest request)
    {
        if (ClosingPosition && request.OriginNodeId != PositionCloseOrder.OriginNodeId)
        {
            return new OrderSubmission(
                Order: null,
                TradingAccount.Reject(RejectionReason.PositionClosing, request.Side, request.Type, request.Tag, request.OriginNodeId, orderId: null));
        }

        OrderSubmission result = _account.Submit(request);

        if (result.Order is { } order)
        {
            order.AwaitSubmission();
            _progress.Add(order.Id, new ExternalOrderProgress());
        }

        return result;
    }

    public Order? Cancel(long orderId)
    {
        ExternalCancelRequest request = RequestCancel(orderId);

        return request.Disposition == ExternalCancelDisposition.Pending ? _account.Orders.Get(orderId) : null;
    }

    public Order? CancelByTag(string tag)
    {
        ExternalCancelRequest request = RequestCancelByTag(tag);

        return request.Disposition == ExternalCancelDisposition.Pending ? _account.Orders.Get(request.OrderId!.Value) : null;
    }

    public OrderExecutedTotals ExecutedTotals(long orderId)
    {
        return _progress.TryGetValue(orderId, out ExternalOrderProgress? progress)
            ? new OrderExecutedTotals(progress.GrossBase, progress.BaseChange, progress.QuoteChange)
            : OrderExecutedTotals.None;
    }

    public ExternalCancelRequest RequestCancel(long orderId)
    {
        return RequestCancel(_account.Orders.Get(orderId));
    }

    public ExternalCancelRequest RequestCancelByTag(string tag)
    {
        return RequestCancel(_account.Orders.GetActive(tag));
    }

    public ExternalOrderUpdateResult ApplyExternalUpdate(ExternalOrderUpdate update, long rejectionAttemptId)
    {
        if (update.Kind == ExternalOrderUpdateKind.FeeCorrection)
        {
            return CorrectFee(update);
        }

        Order? order = _account.Orders.Get(update.OrderId);

        if (order is null)
        {
            return ExternalOrderUpdateResult.Ignored("UnknownOrder");
        }

        if (!order.IsActive)
        {
            return ExternalOrderUpdateResult.Ignored("TerminalOrder");
        }

        ExternalOrderProgress progress = _progress[order.Id];

        return update.Kind switch
        {
            ExternalOrderUpdateKind.Accepted => Accept(order, progress),
            ExternalOrderUpdateKind.LocalAccepted => AcceptLocally(order, progress),
            ExternalOrderUpdateKind.Rejected => Reject(order, progress, update, rejectionAttemptId),
            ExternalOrderUpdateKind.PartialFill or ExternalOrderUpdateKind.FinalFill => Fill(order, progress, update),
            ExternalOrderUpdateKind.CancelConfirmed => ConfirmCancel(order),
            _ => ExternalOrderUpdateResult.Ignored("UnknownUpdate")
        };
    }

    /// <summary>
    ///     Whether the account can take <paramref name="newCapital" /> without leaving less free quote than is reserved.
    /// </summary>
    public bool CanAdjustCapital(decimal newCapital)
    {
        return _account.Portfolio.CanAdjustCapital(newCapital);
    }

    /// <summary>
    ///     Changes the starting capital of a running account (ADR 0025); the difference moves the quote balance.
    /// </summary>
    public void AdjustCapital(decimal newCapital)
    {
        _account.Portfolio.AdjustCapital(newCapital);
    }

    /// <summary>
    ///     A live run that starts over after a forgotten one continues its order identifiers, so no client order identifier
    ///     repeats at the venue. Only before the first order.
    /// </summary>
    public void ContinueOrderIdsAfter(long lastIssuedId)
    {
        _account.Orders.Restore(lastIssuedId, []);
    }

    public ExternalAccountCheckpoint CaptureCheckpoint()
    {
        Portfolio portfolio = _account.Portfolio;

        return new ExternalAccountCheckpoint(
            portfolio.StartingCapital,
            portfolio.QuoteTotal,
            portfolio.QuoteReserved,
            portfolio.BaseTotal,
            portfolio.BaseReserved,
            portfolio.CostBasis,
            portfolio.RealizedPnl,
            portfolio.FeesBase,
            portfolio.FeesQuote,
            portfolio.LastBuyPrice,
            _account.Orders.LastIssuedId,
            _account.Orders.Active.Select(OrderSnapshot.Capture).ToImmutableArray(),
            _account.Orders.Active.Select(order => _progress[order.Id].Capture(order.Id)).ToImmutableArray(),
            _feeReceipts.Count == 0 ? default : _feeReceipts.Values.OrderBy(receipt => receipt.OrderId).ToImmutableArray(),
            portfolio.EntryNotional,
            portfolio.FeesInCost);
    }

    internal OrderRejection RejectSimulatedMarketOrder(long orderId, RejectionReason reason)
    {
        Order order = _account.Orders.Get(orderId) is { Status: OrderStatus.Active, Type: OrderType.Market } active &&
            _progress[orderId].Confirmed
                ? active
                : throw new InvalidOperationException("Only a confirmed active simulated market order can fail at its fill price.");
        Release(order, OrderStatus.Rejected);

        return TradingAccount.Reject(reason, order.Side, order.Type, order.Tag, order.OriginNodeId, order.Id);
    }

    public static ExternallyExecutedAccount Restore(ExternalAccountCheckpoint checkpoint, ExecutionSettings settings, InstrumentRules rules)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        AccountSnapshot.RestoreForExternal(
            checkpoint.StartingCapital,
            checkpoint.QuoteTotal,
            checkpoint.QuoteReserved,
            checkpoint.BaseTotal,
            checkpoint.BaseReserved,
            checkpoint.CostBasis,
            checkpoint.RealizedPnl,
            checkpoint.FeesBase,
            checkpoint.FeesQuote,
            checkpoint.LastBuyPrice,
            checkpoint.Orders,
            allowQuoteShortfall: rules.BuyFeeAsset == FeeAsset.Quote,
            entryNotional: checkpoint.EntryNotional,
            allowShort: settings.Direction == PositionDirection.Short);

        if (checkpoint.Orders.Sum(order => order.Side == settings.OpeningSide ? order.Reservation : 0m) != checkpoint.QuoteReserved ||
            checkpoint.Orders.Sum(order => order.Side != settings.OpeningSide ? order.Reservation : 0m) != checkpoint.BaseReserved ||
            checkpoint.Progress.Length != checkpoint.Orders.Length ||
            checkpoint.Progress.Select(progress => progress.OrderId).Distinct().Count() != checkpoint.Progress.Length ||
            checkpoint.Orders.Any(order => checkpoint.Progress.All(progress => progress.OrderId != order.Id)))
        {
            throw new ArgumentException("Invalid external account checkpoint.", nameof(checkpoint));
        }

        var account = new ExternallyExecutedAccount(settings, rules);
        account._account.Portfolio.Restore(checkpoint);
        account._account.Orders.Restore(checkpoint.NextOrderId, checkpoint.Orders);

        foreach (ExternalOrderProgressCheckpoint progress in checkpoint.Progress)
        {
            account._progress.Add(progress.OrderId, ExternalOrderProgress.Restore(progress));
        }

        foreach (ExternalFeeReceiptCheckpoint receipt in checkpoint.FeeReceipts.IsDefault ? [] : checkpoint.FeeReceipts)
        {
            if (rules.BuyFeeAsset != FeeAsset.Quote || settings.IsMargin || receipt.OrderId <= 0 || receipt.OrderId > checkpoint.NextOrderId ||
                receipt.GrossBase <= 0m || receipt.GrossQuote <= 0m || receipt.Fee < 0m || receipt.RemainingShares < 0m || receipt.PendingFeeReserve < 0m ||
                receipt.RemainingShares > receipt.GrossBase || (receipt.Side == OrderSide.Sell && receipt.RemainingShares != 0m) ||
                !account._feeReceipts.TryAdd(receipt.OrderId, receipt))
            {
                throw new ArgumentException("Invalid external fee receipt.", nameof(checkpoint));
            }

            if (receipt.Slices is { } slices)
            {
                var previousBase = 0m;
                var previousQuote = 0m;

                foreach (ExternalFeeSliceCheckpoint slice in slices)
                {
                    if (slice.GrossBase <= previousBase || slice.GrossQuote <= previousQuote || slice.Fee < 0m ||
                        slice.RemainingShares < 0m || slice.RemainingShares > slice.GrossBase - previousBase ||
                        (receipt.Side == OrderSide.Sell && slice.RemainingShares != 0m))
                    {
                        throw new ArgumentException("Invalid external fee slice.", nameof(checkpoint));
                    }

                    previousBase = slice.GrossBase;
                    previousQuote = slice.GrossQuote;
                }

                if (previousBase != receipt.GrossBase || previousQuote != receipt.GrossQuote || slices.Sum(slice => slice.Fee) != receipt.Fee ||
                    slices.Sum(slice => slice.RemainingShares) != receipt.RemainingShares)
                {
                    throw new ArgumentException("Inconsistent external fee slices.", nameof(checkpoint));
                }
            }
        }

        account.RefreshPendingFeeReserve();

        return account;
    }

    private static ExternalCancelRequest RequestCancel(Order? order)
    {
        if (order is not { IsActive: true })
        {
            return new ExternalCancelRequest(ExternalCancelDisposition.NoTarget, OrderId: null);
        }

        if (order.Status == OrderStatus.PendingCancel)
        {
            return new ExternalCancelRequest(ExternalCancelDisposition.AlreadyPending, order.Id);
        }

        order.AwaitCancellation();

        return new ExternalCancelRequest(ExternalCancelDisposition.Pending, order.Id);
    }

    private static ExternalOrderUpdateResult Accept(Order order, ExternalOrderProgress progress)
    {
        if (progress.Confirmed)
        {
            return ExternalOrderUpdateResult.Ignored("DuplicateAcceptance");
        }

        if (!progress.LocallyAccepted)
        {
            order.ConfirmSubmission();
        }

        progress.Confirmed = true;

        return progress.LocallyAccepted
            ? ExternalOrderUpdateResult.Applied()
            : ExternalOrderUpdateResult.Applied(new LifecycleEmission(EventKind.Submitted, new OrderPayload(OrderSnapshot.Capture(order))));
    }

    private static ExternalOrderUpdateResult AcceptLocally(Order order, ExternalOrderProgress progress)
    {
        if (progress.LocallyAccepted || progress.Confirmed)
        {
            return ExternalOrderUpdateResult.Ignored("DuplicateAcceptance");
        }

        order.ConfirmSubmission();
        progress.LocallyAccepted = true;

        return ExternalOrderUpdateResult.Applied(new LifecycleEmission(EventKind.Submitted, new OrderPayload(OrderSnapshot.Capture(order))));
    }

    private ExternalOrderUpdateResult Reject(Order order, ExternalOrderProgress progress, ExternalOrderUpdate update, long rejectionAttemptId)
    {
        if (progress.Confirmed || progress.GrossBase > 0m)
        {
            return ExternalOrderUpdateResult.Ignored("StaleRejection");
        }

        if (update.Reason is null || string.IsNullOrWhiteSpace(update.Message) || rejectionAttemptId <= 0)
        {
            return ExternalOrderUpdateResult.Ignored("InvalidRejection");
        }

        Release(order, OrderStatus.Rejected);
        var rejection = new OrderRejection(update.Reason.Value, update.Message, order.Side, order.Type, order.Tag, order.OriginNodeId, order.Id);

        return ExternalOrderUpdateResult.Applied(new LifecycleEmission(EventKind.Rejected, new RejectionPayload(rejectionAttemptId, rejection)));
    }

    private ExternalOrderUpdateResult ConfirmCancel(Order order)
    {
        if (order.Status is not (OrderStatus.PendingCancel or OrderStatus.Active))
        {
            return ExternalOrderUpdateResult.Ignored("UnrequestedCancellation");
        }

        Release(order, OrderStatus.Cancelled);

        return ExternalOrderUpdateResult.Applied(new LifecycleEmission(EventKind.Cancelled, new OrderPayload(OrderSnapshot.Capture(order))));
    }

    private ExternalOrderUpdateResult Fill(Order order, ExternalOrderProgress progress, ExternalOrderUpdate update)
    {
        if (!progress.Confirmed)
        {
            return ExternalOrderUpdateResult.Ignored("UnconfirmedFill");
        }

        if (update.CumulativeGrossBase is not { } grossBase || update.CumulativeGrossQuote is not { } grossQuote ||
            update.CumulativeFee is not { } fee || grossBase < progress.GrossBase || grossQuote < progress.GrossQuote || fee < progress.Fee ||
            grossBase <= 0m || grossQuote <= 0m || (order.Quantity is { } quantity && grossBase > quantity) ||
            (order.Side == OrderSide.Buy && Rules.BuyFeeAsset == FeeAsset.Base ? fee > grossBase : fee > grossQuote))
        {
            return ExternalOrderUpdateResult.Ignored("StaleOrInvalidFill");
        }

        decimal deltaBase = grossBase - progress.GrossBase;
        decimal deltaQuote = grossQuote - progress.GrossQuote;
        decimal deltaFee = fee - progress.Fee;
        bool final = update.Kind == ExternalOrderUpdateKind.FinalFill;
        ExternalFeeReceiptCheckpoint? previousReceipt = _feeReceipts.GetValueOrDefault(order.Id);
        bool partitioned = previousReceipt is null || (update.CommissionPartitioned && FeeSlices(previousReceipt).All(slice => !slice.CommissionPending));
        bool pending = update.CommissionPending || !partitioned;
        bool receipts = Rules.BuyFeeAsset == FeeAsset.Quote && !Settings.IsMargin;

        if (receipts && deltaBase > 0m && !partitioned)
        {
            fee = progress.Fee;
            deltaFee = 0m;
        }

        if ((deltaBase == 0m && (!final || deltaQuote != 0m || deltaFee != 0m)) ||
            (deltaBase > 0m && (deltaQuote <= 0m || deltaFee < 0m)) ||
            (Settings.IsMargin
                ? order.Side == Settings.OpeningSide ? order.QuoteBudget is { } budget && grossQuote > budget : deltaBase > order.Reservation
                : order.Side == OrderSide.Buy
                    ? deltaQuote > order.Reservation
                    : deltaBase > order.Reservation))
        {
            return ExternalOrderUpdateResult.Ignored("DuplicateOrInvalidFill");
        }

        if (deltaBase > 0m)
        {
            ApplyFillDelta(order, progress, deltaBase, deltaQuote, deltaFee);
            progress.GrossBase = grossBase;
            progress.GrossQuote = grossQuote;
            progress.Fee = fee;

            if (receipts)
            {
                decimal remaining = previousReceipt?.RemainingShares ?? 0m;
                ExternalFeeSliceCheckpoint[] slices = (previousReceipt is null ? [] : FeeSlices(previousReceipt))
                    .Append(new ExternalFeeSliceCheckpoint(grossBase, grossQuote, deltaFee, order.Side == OrderSide.Buy ? deltaBase : 0m, pending))
                    .ToArray();
                _feeReceipts[order.Id] = new ExternalFeeReceiptCheckpoint(
                    order.Id,
                    order.Side,
                    grossBase,
                    grossQuote,
                    fee,
                    order.Side == OrderSide.Buy ? remaining + deltaBase : 0m,
                    PendingReserve(order.Type, slices),
                    order.Type,
                    slices);
                RefreshPendingFeeReserve();
            }
        }

        if (!final)
        {
            return ExternalOrderUpdateResult.Applied();
        }

        Release(order, OrderStatus.Filled);
        var aggregate = new Fill(
            order.Id,
            order.Tag,
            order.Side,
            order.Type,
            progress.GrossQuote / progress.GrossBase,
            progress.GrossBase,
            progress.BaseChange,
            progress.QuoteChange,
            progress.Fee,
            order.Side == OrderSide.Buy ? Rules.BuyFeeAsset : FeeAsset.Quote,
            progress.RealizedPnl);

        return ExternalOrderUpdateResult.Applied(new LifecycleEmission(EventKind.Filled, new FillPayload(aggregate, order.OriginNodeId)));
    }

    private void ApplyFillDelta(Order order, ExternalOrderProgress progress, decimal grossBase, decimal grossQuote, decimal fee)
    {
        decimal price = grossQuote / grossBase;

        if (Settings.IsMargin)
        {
            ApplyMarginFillDelta(order, progress, grossBase, grossQuote, fee, price);

            return;
        }

        if (order.Side == OrderSide.Buy)
        {
            _account.Portfolio.ReleaseQuote(grossQuote);
            order.ConsumeReservation(grossQuote);

            if (Rules.BuyFeeAsset == FeeAsset.Quote)
            {
                _account.Portfolio.BuyWithQuoteFee(grossQuote, grossBase, fee, price);
                progress.BaseChange += grossBase;
                progress.QuoteChange -= grossQuote + fee;
            }
            else
            {
                _account.Portfolio.Buy(grossQuote, grossBase, fee, price);

                progress.BaseChange += grossBase - fee;
                progress.QuoteChange -= grossQuote;
            }
        }
        else
        {
            if (Rules.BuyFeeAsset == FeeAsset.Quote)
            {
                decimal before = _account.Portfolio.BaseTotal;

                foreach (ExternalFeeReceiptCheckpoint receipt in _feeReceipts.Values.Where(receipt => receipt.Side == OrderSide.Buy).ToArray())
                {
                    ExternalFeeSliceCheckpoint[] retained = FeeSlices(receipt)
                        .Select(slice => slice with
                        {
                            RemainingShares = grossBase == before ? 0m : slice.RemainingShares * (before - grossBase) / before
                        })
                        .ToArray();
                    _feeReceipts[receipt.OrderId] = receipt with { RemainingShares = retained.Sum(slice => slice.RemainingShares), Slices = retained };
                }
            }

            _account.Portfolio.ReleaseBase(grossBase);
            order.ConsumeReservation(grossBase);
            decimal realized = _account.Portfolio.Sell(grossBase, grossQuote, fee);
            progress.BaseChange -= grossBase;
            progress.QuoteChange += grossQuote - fee;
            progress.RealizedPnl += realized;
        }
    }

    /// <summary>
    ///     A fill of a margin run's order (ADR 0061). An entry consumes the margin and fee it reserved for what filled and opens or adds
    ///     to the position; an exit consumes the position it reserved and closes that part. Fees are final quote fees.
    /// </summary>
    private void ApplyMarginFillDelta(Order order, ExternalOrderProgress progress, decimal grossBase, decimal grossQuote, decimal fee, decimal price)
    {
        bool @long = Settings.Direction == PositionDirection.Long;

        if (order.Side == Settings.OpeningSide)
        {
            decimal consumed = Math.Min(order.Reservation, grossQuote / Settings.Leverage!.Value + fee);
            _account.Portfolio.ReleaseQuote(consumed);
            order.ConsumeReservation(consumed);

            if (@long)
            {
                _account.Portfolio.OpenLong(grossQuote, grossBase, fee, price);
                progress.BaseChange += grossBase;
                progress.QuoteChange -= grossQuote + fee;
            }
            else
            {
                _account.Portfolio.OpenShort(grossBase, grossQuote, fee, price);
                progress.BaseChange -= grossBase;
                progress.QuoteChange += grossQuote - fee;
            }

            return;
        }

        _account.Portfolio.ReleaseBase(grossBase);
        order.ConsumeReservation(grossBase);

        if (@long)
        {
            progress.RealizedPnl += _account.Portfolio.Sell(grossBase, grossQuote, fee);
            progress.BaseChange -= grossBase;
            progress.QuoteChange += grossQuote - fee;
        }
        else
        {
            progress.RealizedPnl += _account.Portfolio.Cover(grossBase, grossQuote, fee);
            progress.BaseChange += grossBase;
            progress.QuoteChange -= grossQuote + fee;
        }
    }

    private ExternalOrderUpdateResult CorrectFee(ExternalOrderUpdate update)
    {
        if (Rules.BuyFeeAsset != FeeAsset.Quote || Settings.IsMargin || !_feeReceipts.TryGetValue(update.OrderId, out ExternalFeeReceiptCheckpoint? receipt) ||
            update.CumulativeFee is not { } fee)
        {
            return ExternalOrderUpdateResult.Ignored("InvalidFeeCorrection");
        }

        ExternalFeeSliceCheckpoint[] slices = FeeSlices(receipt).ToArray();
        int index = Array.FindIndex(slices, slice => slice.GrossBase == update.CumulativeGrossBase && slice.GrossQuote == update.CumulativeGrossQuote);

        if (index < 0 || (index > 0 && !update.CommissionPartitioned) || slices.Take(index).Any(slice => slice.CommissionPending))
        {
            return ExternalOrderUpdateResult.Ignored("UnpartitionedFeeCorrection");
        }

        decimal delta = fee - slices.Take(index + 1).Sum(slice => slice.Fee);
        ExternalFeeSliceCheckpoint target = slices[index];

        if (delta < 0m)
        {
            return ExternalOrderUpdateResult.Ignored("InvalidFeeCorrection");
        }

        if (delta == 0m && target.CommissionPending == update.CommissionPending)
        {
            return ExternalOrderUpdateResult.Ignored("DuplicateFeeCorrection");
        }

        decimal batchShares = target.GrossBase - (index == 0 ? 0m : slices[index - 1].GrossBase);
        decimal retained = receipt.Side == OrderSide.Buy ? delta * target.RemainingShares / batchShares : 0m;

        if (delta > 0m)
        {
            _account.Portfolio.CorrectQuoteFee(delta, retained);
        }

        slices[index] = target with { Fee = target.Fee + delta, CommissionPending = update.CommissionPending };
        _feeReceipts[receipt.OrderId] = receipt with
        {
            Fee = receipt.Fee + delta,
            PendingFeeReserve = PendingReserve(receipt.Type, slices),
            Slices = slices
        };
        RefreshPendingFeeReserve();

        if (_progress.TryGetValue(receipt.OrderId, out ExternalOrderProgress? progress))
        {
            progress.Fee += delta;
            progress.QuoteChange -= delta;
            progress.RealizedPnl -= delta - retained;
        }

        return ExternalOrderUpdateResult.Applied();
    }

    private static IReadOnlyList<ExternalFeeSliceCheckpoint> FeeSlices(ExternalFeeReceiptCheckpoint receipt)
    {
        return receipt.Slices ?? [new ExternalFeeSliceCheckpoint(receipt.GrossBase, receipt.GrossQuote, receipt.Fee, receipt.RemainingShares, CommissionPending: true)];
    }

    private decimal PendingReserve(OrderType type, IReadOnlyList<ExternalFeeSliceCheckpoint> slices)
    {
        decimal rate = type == OrderType.Limit ? Settings.LimitFeeRate : Settings.MarketFeeRate;
        var previousQuote = 0m;
        var pending = 0m;

        foreach (ExternalFeeSliceCheckpoint slice in slices)
        {
            if (slice.CommissionPending)
            {
                pending += Math.Max(val1: 0m, (slice.GrossQuote - previousQuote) * rate - slice.Fee);
            }

            previousQuote = slice.GrossQuote;
        }

        return pending;
    }

    private void RefreshPendingFeeReserve()
    {
        _account.Portfolio.SetPendingFeeReserve(_feeReceipts.Values.Sum(receipt => receipt.PendingFeeReserve));
    }

    private void Release(Order order, OrderStatus status)
    {
        if (order.Side == Settings.OpeningSide)
        {
            _account.Portfolio.ReleaseQuote(order.Reservation);
        }
        else
        {
            _account.Portfolio.ReleaseBase(order.Reservation);
        }

        order.Close(status);
        _account.Orders.Remove(order);
    }
}