namespace ProfitGraph.Engine.Graphs;

/// <summary>
///     A typed literal for exact trading graph inputs.
/// </summary>
public sealed record DecimalParamLiteral(decimal Value) : ParamLiteral;