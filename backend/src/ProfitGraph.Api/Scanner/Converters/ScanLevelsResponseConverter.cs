using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Api.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Api.Scanner.Converters;

internal static class ScanLevelsResponseConverter
{
    public static ScanLevelsResponse ToResponse(this ScanLevelsInternal levels)
    {
        return new ScanLevelsResponse(
            levels.Venue,
            levels.Symbol,
            levels.Timeframe,
            levels.From,
            levels.To,
            levels.Status,
            levels.ErrorCode,
            levels.CandleCount,
            ScanSnapshotResponseConverter.Text(levels.Price),
            [.. levels.Levels.Select(ExactGraphNumberParser.Format)],
            ScanSnapshotResponseConverter.Text(levels.Support),
            ScanSnapshotResponseConverter.Text(levels.Resistance),
            ScanSnapshotResponseConverter.Text(levels.DistanceToSupport),
            ScanSnapshotResponseConverter.Text(levels.DistanceToResistance),
            ScanSnapshotResponseConverter.Text(levels.RewardRisk));
    }
}