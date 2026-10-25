using ProfitGraph.Api.Backtesting.Contracts;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     A bot as its owner sees it; decimals are exact invariant strings.
/// </summary>
public sealed record BotResponse(
    Guid Id,
    Guid StrategyId,
    Guid StrategyVersionId,
    Guid CredentialId,
    string Venue,
    string Symbol,
    string Timeframe,
    IReadOnlyDictionary<string, string> Parameters,
    string AllocationCap,
    string MaxOrderQuote,
    string DailyLossLimitQuote,
    string LimitFeeRate,
    string MarketFeeRate,
    DateTimeOffset FeeRatesReadAt,
    bool AllowForeignOrders,
    BotDesiredState DesiredState,
    BotRuntimeStatus RuntimeStatus,
    BotStatusReason? StatusReason,
    IReadOnlyList<string> Diagnostics,
    int RestartAttempts,
    DateTimeOffset? NextRestartAt,
    bool HasRun,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    SellerFeeScheduleResponse? SellerFees);