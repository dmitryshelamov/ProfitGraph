using ProfitGraph.Venues.TInvest.Trading;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     The attempts table in memory, with the same rules: one row per attempt and transitions only from the expected state.
/// </summary>
internal sealed class MemoryTInvestOrderAttempts : ITInvestOrderAttempts
{
    private readonly Dictionary<Guid, TInvestOrderAttempt> _rows = new Dictionary<Guid, TInvestOrderAttempt>();

    public IReadOnlyList<TInvestOrderAttempt> All
    {
        get
        {
            lock (_rows)
            {
                return [.. _rows.Values.OrderBy(row => row.EngineOrderId).ThenBy(row => row.Attempt)];
            }
        }
    }

    public Task<bool> Add(TInvestOrderAttempt attempt, CancellationToken cancellationToken)
    {
        lock (_rows)
        {
            if (_rows.Values.Any(row => row.BotId == attempt.BotId && row.EngineOrderId == attempt.EngineOrderId && row.Attempt == attempt.Attempt))
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(_rows.TryAdd(attempt.RequestId, attempt));
        }
    }

    public Task<TInvestOrderAttempt?> Latest(Guid botId, long engineOrderId, CancellationToken cancellationToken)
    {
        lock (_rows)
        {
            return Task.FromResult(_rows.Values.Where(row => row.BotId == botId && row.EngineOrderId == engineOrderId).MaxBy(row => row.Attempt));
        }
    }

    public Task<TInvestOrderAttempt?> ByRequestId(Guid requestId, CancellationToken cancellationToken)
    {
        lock (_rows)
        {
            return Task.FromResult(_rows.GetValueOrDefault(requestId));
        }
    }

    public Task<bool> Transition(TInvestOrderAttempt next, TInvestOrderAttemptState expected, CancellationToken cancellationToken)
    {
        lock (_rows)
        {
            if (!_rows.TryGetValue(next.RequestId, out TInvestOrderAttempt? current) || current.State != expected)
            {
                return Task.FromResult(false);
            }

            _rows[next.RequestId] = current with
            {
                State = next.State,
                ExchangeOrderId = next.ExchangeOrderId,
                BrokerCode = next.BrokerCode,
                Price = next.Price,
                WaitingCode = next.WaitingCode,
                BandLow = next.BandLow,
                BandHigh = next.BandHigh,
                CancelledBy = next.CancelledBy,
                CarriedFrom = next.CarriedFrom,
                ExecutedLots = next.ExecutedLots,
                ExecutedQuote = next.ExecutedQuote,
                ExecutedFee = next.ExecutedFee,
                UpdatedAt = next.UpdatedAt
            };

            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<TInvestOrderAttempt>> Waiting(CancellationToken cancellationToken)
    {
        lock (_rows)
        {
            IReadOnlyList<TInvestOrderAttempt> waiting = [.. _rows.Values.Where(row => row.WaitingCode is not null).OrderBy(row => row.EngineOrderId)];

            return Task.FromResult(waiting);
        }
    }

    public Task<IReadOnlyList<TInvestOrderAttempt>> Working(CancellationToken cancellationToken)
    {
        lock (_rows)
        {
            IReadOnlyList<TInvestOrderAttempt> working = [.. _rows.Values.Where(row => row.State == TInvestOrderAttemptState.Active).OrderBy(row => row.EngineOrderId)];

            return Task.FromResult(working);
        }
    }

    public Task<IReadOnlyList<TInvestOrderAttempt>> ForOrder(Guid botId, long engineOrderId, CancellationToken cancellationToken)
    {
        lock (_rows)
        {
            IReadOnlyList<TInvestOrderAttempt> attempts =
                [.. _rows.Values.Where(row => row.BotId == botId && row.EngineOrderId == engineOrderId).OrderBy(row => row.Attempt)];

            return Task.FromResult(attempts);
        }
    }

    public Task<IReadOnlyList<TInvestOrderAttempt>> ExecutedOnShare(Guid botId, string instrumentUid, DateTimeOffset since, CancellationToken cancellationToken)
    {
        lock (_rows)
        {
            IReadOnlyList<TInvestOrderAttempt> executed =
            [
                .. _rows.Values.Where(row => row.BotId == botId && row.InstrumentUid == instrumentUid && row.ExecutedLots > 0 && row.UpdatedAt >= since)
                    .OrderBy(row => row.EngineOrderId)
                    .ThenBy(row => row.Attempt)
            ];

            return Task.FromResult(executed);
        }
    }

    public Task<IReadOnlyList<TInvestOrderAttempt>> PendingFees(DateTimeOffset since, CancellationToken cancellationToken)
    {
        lock (_rows)
        {
            IReadOnlyList<TInvestOrderAttempt> pending =
            [
                .. _rows.Values.Where(row => row is { State: TInvestOrderAttemptState.Final, ExecutedLots: > 0, ExecutedFee: 0m } && row.UpdatedAt >= since)
                    .OrderBy(row => row.EngineOrderId)
                    .ThenBy(row => row.Attempt)
            ];

            return Task.FromResult(pending);
        }
    }

    public Task<IReadOnlyDictionary<Guid, Guid>> Owners(IReadOnlyCollection<Guid> requestIds, CancellationToken cancellationToken)
    {
        lock (_rows)
        {
            IReadOnlyDictionary<Guid, Guid> owners = _rows.Values.Where(row => requestIds.Contains(row.RequestId)).ToDictionary(row => row.RequestId, row => row.BotId);

            return Task.FromResult(owners);
        }
    }
}