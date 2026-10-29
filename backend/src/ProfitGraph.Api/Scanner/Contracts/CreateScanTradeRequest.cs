namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     Creates a trade from a card (ADR 0060) with the prices the card showed from the levels of <see cref="Timeframe" />, as exact
///     decimal strings.
/// </summary>
public sealed record CreateScanTradeRequest(string Symbol, string Timeframe, string Scenario, string Entry, string Stop, string Target);