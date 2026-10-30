using ProfitGraph.Domain.Scanner.AutoTrades;
using ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Scanner.Repositories.Converters;

/// <summary>
///     A skipped request stores its skip reason as its status.
/// </summary>
internal static class ScanAutoTradeRequestDbConverter
{
    public static ScanAutoTradeRequest ToDomain(this ScanAutoTradeRequestDb row)
    {
        bool known = Enum.TryParse(row.Status, out ScanAutoTradeRequestStatus status);
        ScanAutoTradeSkip? skip = known ? null : Enum.Parse<ScanAutoTradeSkip>(row.Status);

        return ScanAutoTradeRequest.Restore(
            new ScanAutoTradeRequestId(row.Id),
            new ScanAutoTradeRuleId(row.RuleId),
            row.Symbol,
            row.SnapshotAt,
            row.Price,
            row.CreatedAt,
            known ? status : ScanAutoTradeRequestStatus.Skipped,
            skip,
            row.ErrorCode,
            row.TradeId is { } trade ? new ScanPaperTradeId(trade) : null,
            row.HandledAt);
    }

    public static string StatusText(this ScanAutoTradeRequest request)
    {
        return request.Skip?.ToString() ?? request.Status.ToString();
    }

    public static object ToParameters(this ScanAutoTradeRequest request)
    {
        return new
        {
            Id = request.Id.Value,
            RuleId = request.RuleId.Value,
            request.Symbol,
            request.SnapshotAt,
            request.Price,
            request.CreatedAt,
            Status = request.StatusText(),
            request.ErrorCode,
            TradeId = request.PaperTradeId?.Value,
            request.HandledAt
        };
    }
}