namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Which way a run trades (ADR 0061): a long run buys first and never holds less than nothing; a short run sells first on a
///     margin venue and never holds more than nothing.
/// </summary>
public enum PositionDirection
{
    Long,
    Short
}