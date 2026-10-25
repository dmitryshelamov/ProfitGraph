namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;

/// <summary>
///     A seller fee schedule inside a stored configuration; absent when the run uses limit and market rates.
/// </summary>
internal sealed class SellerFeeScheduleDb
{
    public decimal[] Rates { get; init; } = null!;
    public decimal Minimum { get; init; }
}