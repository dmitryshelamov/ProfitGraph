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

    public Task<IReadOnlyDictionary<Guid, Guid>> Owners(IReadOnlyCollection<Guid> requestIds, CancellationToken cancellationToken)
    {
        lock (_rows)
        {
            IReadOnlyDictionary<Guid, Guid> owners = _rows.Values.Where(row => requestIds.Contains(row.RequestId)).ToDictionary(row => row.RequestId, row => row.BotId);

            return Task.FromResult(owners);
        }
    }
}