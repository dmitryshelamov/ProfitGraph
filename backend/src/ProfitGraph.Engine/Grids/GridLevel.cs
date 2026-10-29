namespace ProfitGraph.Engine.Grids;

/// <summary>
///     The mutable state of one level of a running grid.
/// </summary>
internal sealed class GridLevel
{
    public decimal HeldBase { get; set; }
    public decimal PaidQuote { get; set; }
    public bool Blocked { get; set; }

    public bool IsEmpty => HeldBase <= 0m && !Blocked;
}