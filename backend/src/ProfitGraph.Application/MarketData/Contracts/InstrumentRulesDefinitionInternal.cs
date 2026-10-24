using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.MarketData.Contracts;

/// <summary>
///     A venue's current trading rules of one instrument in the engine's terms, with the original precision increments.
/// </summary>
public sealed record InstrumentRulesDefinitionInternal(
    string Venue,
    string Source,
    string Market,
    string Symbol,
    string BaseAsset,
    string QuoteAsset,
    decimal BasePrecisionIncrement,
    decimal QuotePrecisionIncrement,
    InstrumentRules Rules);