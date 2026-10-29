namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     Creates a trade from a card (ADR 0060) with the prices the card showed from the levels of <see cref="Timeframe" />, as exact
///     decimal strings, and its direction, <c>Long</c> unless <c>Short</c> (ADR 0061).
/// </summary>
public sealed record CreateScanTradeRequest(string Symbol, string Timeframe, string Scenario, string Entry, string Stop, string Target, string? Direction = null);