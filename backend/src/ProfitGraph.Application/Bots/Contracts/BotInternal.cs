using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.Contracts;

public sealed record BotInternal(
    Guid Id,
    Guid StrategyId,
    Guid StrategyVersionId,
    Guid CredentialId,
    string Venue,
    string Symbol,
    string Timeframe,
    IReadOnlyDictionary<string, decimal> Parameters,
    decimal AllocationCap,
    decimal MaxOrderQuote,
    decimal DailyLossLimitQuote,
    decimal LimitFeeRate,
    decimal MarketFeeRate,
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
    DateTimeOffset UpdatedAt);