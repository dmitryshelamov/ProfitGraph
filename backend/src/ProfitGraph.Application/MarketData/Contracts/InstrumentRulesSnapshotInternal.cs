namespace ProfitGraph.Application.MarketData.Contracts;

/// <summary>
///     Rules as the venue reported them at <paramref name="CapturedAt" />; never updated, and not a record of the rules
///     that applied in the past.
/// </summary>
public sealed record InstrumentRulesSnapshotInternal(Guid Id, InstrumentRulesDefinitionInternal Definition, DateTimeOffset CapturedAt);