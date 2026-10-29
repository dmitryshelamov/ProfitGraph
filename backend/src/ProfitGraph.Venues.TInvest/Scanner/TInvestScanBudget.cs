using Microsoft.Extensions.Options;

namespace ProfitGraph.Venues.TInvest.Scanner;

/// <summary>
///     The market scanner's shares of the T-Invest token's limits (ADR 0062): its requests and its candle subscriptions.
/// </summary>
internal sealed class TInvestScanBudget(IOptions<TInvestOptions> options, TimeProvider clock)
{
    public TInvestScanPacer Requests { get; } = new TInvestScanPacer(options.Value.Scanner.RequestsPerMinute, clock);

    public TInvestScanPacer Subscriptions { get; } = new TInvestScanPacer(options.Value.Scanner.SubscriptionsPerMinute, clock);
}