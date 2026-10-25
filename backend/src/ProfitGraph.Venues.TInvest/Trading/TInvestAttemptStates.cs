using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     Records what T-Invest reported for an attempt: the exchange order ID, and <see cref="TInvestOrderAttemptState.Final" />
///     once the state can no longer change or <see cref="TInvestOrderAttemptState.Active" /> before. A final attempt stays
///     final.
/// </summary>
internal static class TInvestAttemptStates
{
    public static async Task<TInvestOrderAttempt> Record(
        ITInvestOrderAttempts attempts,
        TInvestOrderAttempt attempt,
        OrderState state,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        TInvestOrderAttemptState next = TInvestOrderTranslation.IsFinal(state) ? TInvestOrderAttemptState.Final : TInvestOrderAttemptState.Active;
        string? exchangeOrderId = string.IsNullOrEmpty(state.OrderId) ? attempt.ExchangeOrderId : state.OrderId;

        if (attempt.State == TInvestOrderAttemptState.Final || (attempt.State == next && attempt.ExchangeOrderId == exchangeOrderId))
        {
            return attempt;
        }

        TInvestOrderAttempt recorded = attempt with { State = next, ExchangeOrderId = exchangeOrderId, UpdatedAt = now };

        return await attempts.Transition(recorded, attempt.State, cancellationToken) ? recorded : attempt;
    }
}