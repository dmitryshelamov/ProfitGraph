using System.Text.Json.Serialization;

namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;

/// <summary>
///     The frozen configuration as stored in <c>backtest_runs.configuration</c>.
/// </summary>
internal sealed class BacktestConfigurationDb
{
    public string Venue { get; init; } = null!;
    public string Symbol { get; init; } = null!;
    public string Timeframe { get; init; } = null!;
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public DateTimeOffset ObservedAt { get; init; }
    public Dictionary<string, decimal> Parameters { get; init; } = null!;
    public decimal StartingCapital { get; init; }
    public decimal Slippage { get; init; }
    public decimal LimitFeeRate { get; init; }
    public decimal MarketFeeRate { get; init; }
    public decimal OutOfSampleFraction { get; init; }
    public int WarmupCandles { get; init; }

    /// <summary>
    ///     Omitted when absent, so a run with limit and market rates keeps the stored shape it had before seller fees.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SellerFeeScheduleDb? SellerFees { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? SettlementHoldSeconds { get; init; }

    public string ExecutionPolicy { get; init; } = null!;
    public string Fingerprint { get; init; } = null!;
}