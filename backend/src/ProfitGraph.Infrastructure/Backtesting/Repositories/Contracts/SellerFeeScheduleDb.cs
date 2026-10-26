using System.Text.Json.Serialization;

namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;

/// <summary>
///     A seller fee schedule inside a stored configuration or bot; absent when the run uses limit and market rates. A null
///     increment is one minor unit and is not written.
/// </summary>
internal sealed class SellerFeeScheduleDb
{
    public decimal[] Rates { get; init; } = null!;
    public decimal Minimum { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Increment { get; init; }
}