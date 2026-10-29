namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     When replay considers a resting limit order executable (ADR 0057): on <see cref="Touch" /> once the price path reaches
///     its limit, on <see cref="Through" /> only once the path trades strictly beyond it.
/// </summary>
public enum LimitFillMode
{
    Touch,
    Through
}