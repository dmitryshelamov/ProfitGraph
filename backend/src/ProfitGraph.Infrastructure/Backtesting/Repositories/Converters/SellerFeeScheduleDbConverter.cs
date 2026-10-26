using ProfitGraph.Engine.Accounting;
using ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Converters;

/// <summary>
///     Stores a seller fee schedule of a backtest configuration or a bot. An increment of one minor unit is left out, so the
///     JSON of such a schedule is the one written before increments existed and that JSON reads back with an increment of one.
/// </summary>
internal static class SellerFeeScheduleDbConverter
{
    public static SellerFeeScheduleDb ToDb(this SellerFeeSchedule fees)
    {
        return new SellerFeeScheduleDb { Rates = [.. fees.Rates], Minimum = fees.Minimum, Increment = fees.Increment == 1 ? null : fees.Increment };
    }

    public static SellerFeeSchedule ToDomain(this SellerFeeScheduleDb row)
    {
        return new SellerFeeSchedule(row.Rates, row.Minimum, row.Increment ?? 1);
    }
}