namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     Creates a stopped bot; decimals are exact invariant strings. Parameters give a value for every parameter of the
///     strategy's latest version. The venue comes from the credential.
/// </summary>
public sealed record CreateBotRequest(
    Guid StrategyId,
    Guid CredentialId,
    string? Symbol,
    string? Timeframe,
    IReadOnlyDictionary<string, string>? Parameters,
    string? AllocationCap,
    string? MaxOrderQuote,
    string? DailyLossLimitQuote,
    bool AllowForeignOrders);