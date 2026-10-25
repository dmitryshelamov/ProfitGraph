using System.Text.Json;

using ProfitGraph.Api.Backtesting.Contracts;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     A bot as its owner sees it; decimals are exact invariant strings. <see cref="VenueSettings" /> is the venue module's
///     own settings object in version <see cref="VenueSettingsVersion" />, empty for a venue without settings.
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
    SellerFeeScheduleResponse? SellerFees,
    int VenueSettingsVersion,
    JsonElement VenueSettings);