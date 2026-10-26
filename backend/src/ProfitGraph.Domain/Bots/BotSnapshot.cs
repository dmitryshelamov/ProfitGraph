using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     Every stored field of a bot, for restoring it from storage and for writing it back.
/// </summary>
public sealed record BotSnapshot(
    BotId Id,
    UserId OwnerId,
    StrategyId StrategyId,
    StrategyVersionId StrategyVersionId,
    ExchangeCredentialId CredentialId,
    CandleSeries Series,
    BotParameters Parameters,
    decimal AllocationCap,
    BotRiskLimits RiskLimits,
    BotFeeRates FeeRates,
    bool AllowForeignOrders,
    BotDesiredState DesiredState,
    BotRuntimeStatus RuntimeStatus,
    BotStatusReason? StatusReason,
    BotDiagnostics Diagnostics,
    int RestartAttempts,
    DateTimeOffset? NextRestartAt,
    bool HasRun,
    DateTimeOffset? FirstStartedAt,
    DailyLossBaseline? DayBaseline,
    StrategyVersionId? RejectedVersionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    BotVenueSettings? VenueSettings = null,
    BotName? Name = null);