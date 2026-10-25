using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Metrics;

namespace ProfitGraph.Engine.Replay;

/// <summary>
///     One invocation's monotonic cursor and committed fact journal; terminal cleanup never dispatches strategy events.
/// </summary>
internal sealed class BacktestRunState
{
    private readonly BacktestInput _input;
    private readonly BacktestRunOptions _options;
    private readonly IBacktestObserver? _observer;
    private readonly CancellationToken _token;
    private readonly TradingAccount _account;
    private readonly List<BacktestLedgerEntry>? _ledger;
    private readonly List<BacktestDiagnostic> _diagnostics = new List<BacktestDiagnostic>();
    private readonly HashSet<(long Cause, long Attempt)> _capturedActions = new HashSet<(long Cause, long Attempt)>();
    private readonly Dictionary<EventPayload, EventOccurrence> _eventsScratch = new Dictionary<EventPayload, EventOccurrence>(ReferenceEqualityComparer.Instance);
    private readonly List<BacktestLedgerEntry> _ledgerScratch = new List<BacktestLedgerEntry>();
    private readonly Dictionary<long, Fill> _partialExecutions = new Dictionary<long, Fill>();
    private CausalDispatcher? _dispatcher;
    private ReachedMarketContext? _capturedCursor;
    private Candle _candle = null!;
    private int _candleIndex;
    private PricePhase _phase;
    private decimal _marketPrice;
    private long _sequence;
    private int _processed;
    private int _fillCount;
    private int _orderCount;
    private bool _indicatorsReady;

    private ReachedMarketContext Cursor => _capturedCursor ??= Context(_candle, _candleIndex, _phase, _marketPrice);

    public BacktestRunState(BacktestInput input, IBacktestObserver? observer, CancellationToken token, BacktestRunOptions options)
    {
        _input = input;
        _options = options;
        _observer = observer;
        _token = token;
        _ledger = options.RetainLedger ? new List<BacktestLedgerEntry>() : null;
        _account = new TradingAccount(input.Settings, input.Rules);
        SetCursor(input.Replay[0], index: 0, PricePhase.Open, input.Replay[0].Open);
    }

    public BacktestResult Run()
    {
        var outcome = BacktestOutcome.Completed;
        BacktestDiagnostic? failure = null;

        try
        {
            _dispatcher = new CausalDispatcher(_input.Graph, _account, _input.Selections);
            var historyIndex = 0;

            foreach (Candle candle in _input.Warmup)
            {
                _token.ThrowIfCancellationRequested();
                _dispatcher.CommitClosedCandle(candle, historyIndex++);
            }

            for (var index = 0; index < _input.Replay.Count; index++)
            {
                _token.ThrowIfCancellationRequested();
                Candle candle = _input.Replay[index];
                SetCursor(candle, index, PricePhase.Open, candle.Open);
                _dispatcher.BeginReplayOpen(index, _marketPrice);

                if (index == 0)
                {
                    _dispatcher.EnqueueExternal(EventKind.Start, new StartPayload(), Cursor);
                    Drain();
                }

                // Nothing traded on a candle without trades (ADR 0023, TC2), so its orders wait for a real candle.
                if (!candle.NoTrades)
                {
                    PricePath.Validate(candle);
                    MatchCurrentPoint();
                    TraverseSegment(candle.Open, candle.Low, PricePhase.OpenToLow, index);
                    TraverseSegment(candle.Low, candle.High, PricePhase.LowToHigh, index);
                    TraverseSegment(candle.High, candle.Close, PricePhase.HighToClose, index);
                }

                SetCursor(candle, index, PricePhase.Close, candle.Close);
                _dispatcher.CommitClosedCandle(candle, historyIndex++);
                _indicatorsReady |= _dispatcher.AreIndicatorsReady;
                _dispatcher.BeginClose(Cursor);
                _dispatcher.EnqueueExternal(EventKind.CandleClosed, new CandlePayload(candle), Cursor);
                Drain();

                if (index == _input.Replay.Count - 1)
                {
                    ReleaseAll();
                }

                ObserveCandle();
            }

            if (!_indicatorsReady)
            {
                AddDiagnostic(new BacktestDiagnostic("IndicatorsNeverReady", "History completed before all indicator values became available."));
            }
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested)
        {
            outcome = BacktestOutcome.Cancelled;
            failure = new BacktestDiagnostic("Cancelled", "Replay was cancelled at the reached market cursor.", CandleIndex: _candleIndex);
            _diagnostics.Add(failure);
        }
        catch (Exception exception)
        {
            outcome = BacktestOutcome.Failed;
            failure = Diagnostic(exception);
            _diagnostics.Add(failure);
        }

        try
        {
            ReleaseAll();
        }
        catch (Exception exception)
        {
            outcome = BacktestOutcome.Failed;
            failure = Diagnostic(exception);
            _diagnostics.Add(failure);
        }

        var terminal = new BacktestTerminalSnapshot(outcome, _processed, Cursor, AccountSnapshot.Capture(_account), failure);

        if (terminal.Equity is null)
        {
            failure = terminal.Diagnostic;

            if (!_diagnostics.Any(diagnostic => diagnostic.Code == "ValuationOverflow"))
            {
                _diagnostics.Add(failure!);
            }
        }

        if (failure is not null)
        {
            try
            {
                _observer?.OnDiagnostic(failure);
            }
            catch (Exception exception)
            {
                failure = new BacktestDiagnostic("ObserverFailure", exception.Message, CandleIndex: _candleIndex);
                _diagnostics.Add(failure);
                terminal = terminal with { Outcome = BacktestOutcome.Failed, Diagnostic = failure };
            }
        }

        try
        {
            _observer?.OnTerminal(terminal);
        }
        catch (Exception exception)
        {
            failure = new BacktestDiagnostic("ObserverFailure", exception.Message, CandleIndex: _candleIndex);
            _diagnostics.Add(failure);
            terminal = terminal with { Outcome = BacktestOutcome.Failed, Diagnostic = failure };
        }

        return new BacktestResult(terminal, _ledger ?? Enumerable.Empty<BacktestLedgerEntry>(), _diagnostics, _input.SampleRanges, _sequence, _fillCount, _orderCount);
    }

    private void ObserveCandle()
    {
        long sequence = checked(_processed + 1);

        if (_observer is IBacktestMetricsObserver metricsObserver)
        {
            Portfolio portfolio = _account.Portfolio;
            AccountValuation valuation = AccountValuation.Capture(portfolio, _marketPrice);
            var observation = new CandleMetricObservation(
                sequence,
                _candleIndex,
                portfolio.StartingCapital,
                valuation.Equity,
                valuation.UnrealizedPnl,
                portfolio.RealizedPnl,
                portfolio.FeesBase,
                portfolio.FeesQuote);
            _processed++;
            metricsObserver.OnCandleMetrics(observation);
        }
        else if (_observer is not null)
        {
            var snapshot = new CandleSnapshot(sequence, Cursor, AccountSnapshot.Capture(_account));
            _processed++;
            _observer.OnCandleProcessed(snapshot);
        }
        else
        {
            // Validate at every close even when no projection is requested, preserving the reached overflow cursor.
            AccountValuation.Capture(_account.Portfolio, _marketPrice);
            _processed++;
        }
    }

    private void RecordLedger(BacktestLedgerEntry entry)
    {
        _ledger?.Add(entry);

        if (entry.Kind == BacktestLedgerKind.Filled)
        {
            _fillCount = checked(_fillCount + 1);
        }
        else if (entry.Kind == BacktestLedgerKind.Submitted)
        {
            _orderCount = checked(_orderCount + 1);
        }
    }

    private void MatchCurrentPoint()
    {
        while (true)
        {
            _token.ThrowIfCancellationRequested();
            Order? market = null;
            IReadOnlyList<Order> active = _account.Orders.Active;

            for (var orderIndex = 0; orderIndex < active.Count; orderIndex++)
            {
                Order candidate = active[orderIndex];

                if (candidate.Type == OrderType.Market && candidate.IsEligibleAt(_candleIndex) && _account.CanExecute(candidate) &&
                    (market is null || candidate.Id < market.Id))
                {
                    market = candidate;
                }
            }

            long? id = market?.Id ?? OrderMatcher.NextAtPrice(_marketPrice, _account, _candleIndex)?.OrderId;

            if (!id.HasValue)
            {
                return;
            }

            Order order = _account.Orders.Get(id.Value)!;
            OrderSnapshot original = OrderSnapshot.Capture(order);
            OrderExecution execution;

            try
            {
                execution = _account.Execute(order.Id, _marketPrice);
            }
            catch (Exception exception) when (exception is ArithmeticException or InvalidOperationException)
            {
                throw new GraphExecutionException(order.OriginNodeId, _candleIndex, exception.Message, exception);
            }

            var entry = new BacktestLedgerEntry(
                checked(++_sequence),
                Cursor,
                execution.IsFilled ? BacktestLedgerKind.Filled : BacktestLedgerKind.Rejected,
                OrderSnapshot.Capture(order),
                execution.Fill,
                execution.Rejection);
            int ledgerIndex = _ledger?.Count ?? 0;
            RecordLedger(entry);
            OrderExecution publication = execution;

            if (execution.Fill is { } delta)
            {
                if (order.IsActive)
                {
                    Fill aggregate = _partialExecutions.TryGetValue(order.Id, out Fill? previous) ? Aggregate(previous, delta) : delta;
                    _partialExecutions[order.Id] = aggregate;
                    _observer?.OnLedger(entry);

                    continue;
                }

                if (_partialExecutions.Remove(order.Id, out Fill? partial))
                {
                    publication = new OrderExecution(Aggregate(partial, delta), Rejection: null);
                }
            }
            else
            {
                _partialExecutions.Remove(order.Id);
            }

            try
            {
                EventOccurrence occurrence = _dispatcher!.PublishExecution(original, publication, Cursor);
                entry = entry with
                {
                    OccurrenceId = occurrence.Id,
                    CausationId = occurrence.CausationId,
                    AttemptId = (occurrence.Payload as RejectionPayload)?.AttemptId
                };

                if (_ledger is not null)
                {
                    _ledger[ledgerIndex] = entry;
                }
            }
            catch
            {
                try
                {
                    _observer?.OnLedger(entry);
                }
                catch (Exception observerFailure)
                {
                    _diagnostics.Add(new BacktestDiagnostic("ObserverFailure", observerFailure.Message, CandleIndex: _candleIndex));
                }

                throw;
            }

            _observer?.OnLedger(entry);
            _token.ThrowIfCancellationRequested();
            Drain();
        }
    }

    private static Fill Aggregate(Fill previous, Fill delta)
    {
        decimal grossBase = previous.GrossBase + delta.GrossBase;

        return delta with
        {
            Price = (previous.Price * previous.GrossBase + delta.Price * delta.GrossBase) / grossBase,
            GrossBase = grossBase,
            BaseChange = previous.BaseChange + delta.BaseChange,
            QuoteChange = previous.QuoteChange + delta.QuoteChange,
            Fee = previous.Fee + delta.Fee,
            RealizedPnl = previous.RealizedPnl + delta.RealizedPnl
        };
    }

    private void Drain()
    {
        IReadOnlyList<AppliedActionFact> actions;
        IReadOnlyList<EventOccurrence> occurrences;
        DispatchBatch? retainedBatch = null;

        try
        {
            if (_options.RetainDispatchProjections)
            {
                retainedBatch = _dispatcher!.Drain(_token);
                actions = retainedBatch.AppliedActions;
                occurrences = retainedBatch.Occurrences;
            }
            else
            {
                ReplayDispatchBatch batch = _dispatcher!.DrainReplayIntoBuffers(_token);
                actions = batch.AppliedActions;
                occurrences = batch.Occurrences;
            }
        }
        catch
        {
            try
            {
                Capture(_dispatcher!.LastBatch);
            }
            catch (Exception observerFailure)
            {
                _diagnostics.Add(new BacktestDiagnostic("ObserverFailure", observerFailure.Message, CandleIndex: _candleIndex));
            }

            throw;
        }

        if (_options.DispatchObserver is not null)
        {
            _options.DispatchObserver(retainedBatch ?? _dispatcher!.LastBatch);
        }

        Capture(actions, occurrences);
    }

    private void Capture(DispatchBatch batch)
    {
        Capture(batch.AppliedActions, batch.Occurrences);
    }

    private void Capture(IReadOnlyList<AppliedActionFact> appliedActions, IReadOnlyList<EventOccurrence> occurrences)
    {
        if (appliedActions.Count == 0)
        {
            return;
        }

        // Every drain starts a fresh action journal; retain deduplication only while capturing that journal.
        _capturedActions.Clear();
        _eventsScratch.Clear();
        _ledgerScratch.Clear();

        for (var occurrenceIndex = 0; occurrenceIndex < occurrences.Count; occurrenceIndex++)
        {
            EventOccurrence occurrence = occurrences[occurrenceIndex];
            _eventsScratch[occurrence.Payload] = occurrence;
        }

        for (var actionIndex = 0; actionIndex < appliedActions.Count; actionIndex++)
        {
            AppliedActionFact action = appliedActions[actionIndex];

            if (!_capturedActions.Add((action.CauseId, action.AttemptId)))
            {
                continue;
            }

            for (var emissionIndex = 0; emissionIndex < action.Results.Count; emissionIndex++)
            {
                LifecycleEmission emission = action.Results[emissionIndex];
                BacktestLedgerKind? kind = emission.Kind switch
                {
                    EventKind.Submitted => BacktestLedgerKind.Submitted,
                    EventKind.Cancelled => BacktestLedgerKind.Cancelled,
                    EventKind.Rejected => BacktestLedgerKind.Rejected,
                    _ => null
                };

                if (!kind.HasValue)
                {
                    continue;
                }

                _eventsScratch.TryGetValue(emission.Payload, out EventOccurrence? occurrence);
                var entry = new BacktestLedgerEntry(
                    checked(++_sequence),
                    Cursor,
                    kind.Value,
                    (emission.Payload as OrderPayload)?.Order,
                    Rejection: (emission.Payload as RejectionPayload)?.Rejection,
                    AttemptId: action.AttemptId,
                    OccurrenceId: occurrence?.Id,
                    CausationId: action.CauseId);
                RecordLedger(entry);

                if (entry.Kind is BacktestLedgerKind.Cancelled or BacktestLedgerKind.Rejected)
                {
                    long? orderId = entry.Order?.Id ?? entry.Rejection?.OrderId;

                    if (orderId.HasValue)
                    {
                        _partialExecutions.Remove(orderId.Value);
                    }
                }

                _ledgerScratch.Add(entry);
            }
        }

        for (var entryIndex = 0; entryIndex < _ledgerScratch.Count; entryIndex++)
        {
            _observer?.OnLedger(_ledgerScratch[entryIndex]);
        }
    }

    private void TraverseSegment(decimal start, decimal end, PricePhase phase, int index)
    {
        if (start == end)
        {
            return;
        }

        Move(phase, _marketPrice);

        while (OrderMatcher.Next(start, end, _marketPrice, _account, index) is { } candidate)
        {
            Move(phase, candidate.PathPrice);
            MatchCurrentPoint();
        }

        Move(phase, end);
    }

    private void SetCursor(Candle candle, int index, PricePhase phase, decimal price)
    {
        _candle = candle;
        _candleIndex = index;
        _phase = phase;
        _marketPrice = price;
        _capturedCursor = null;

        // A point inside or at the end of a candle is known only at its close; settlement holds count from there.
        _account.AdvanceMarketTime(phase == PricePhase.Open ? candle.OpenTime : candle.OpenTime.Add(_input.Timeframe.Duration));
    }

    private void Move(PricePhase phase, decimal price)
    {
        _token.ThrowIfCancellationRequested();
        SetCursor(_candle, _candleIndex, phase, price);
        _dispatcher!.AdvancePrice(price);
    }

    private void ReleaseAll()
    {
        var released = new List<BacktestLedgerEntry>();

        foreach (Order order in _account.ReleaseAll())
        {
            var entry = new BacktestLedgerEntry(checked(++_sequence), Cursor, BacktestLedgerKind.ReleasedTerminal, OrderSnapshot.Capture(order));
            RecordLedger(entry);
            released.Add(entry);
        }

        foreach (BacktestLedgerEntry entry in released)
        {
            _observer?.OnLedger(entry);
        }
    }

    private void AddDiagnostic(BacktestDiagnostic diagnostic)
    {
        _diagnostics.Add(diagnostic);
        _observer?.OnDiagnostic(diagnostic);
    }

    private BacktestDiagnostic Diagnostic(Exception exception)
    {
        return exception switch
        {
            BacktestValuationException valuation => new BacktestDiagnostic("ValuationOverflow", valuation.Message, CandleIndex: _candleIndex),
            GraphExecutionException graph => new BacktestDiagnostic("GraphExecution", graph.Message, graph.NodeId, graph.BarIndex, graph.OccurrenceId),
            CascadeLimitException guard => new BacktestDiagnostic("CascadeLimit", guard.Message, guard.NodeId, _candleIndex, CausationId: guard.CausationId),
            _ => new BacktestDiagnostic(exception.GetType().Name, exception.Message, CandleIndex: _candleIndex)
        };
    }

    private ReachedMarketContext Context(Candle candle, int index, PricePhase phase, decimal price)
    {
        return new ReachedMarketContext(
            candle,
            index,
            candle.OpenTime.Add(_input.Timeframe.Duration),
            phase,
            price,
            phase == PricePhase.Close ? checked(index + 1) : index);
    }
}