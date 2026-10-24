using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Infrastructure.Strategies.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Strategies.Repositories.Converters;

internal static class StrategySummaryDbConverter
{
    public static StrategySummary ToDomain(this StrategySummaryDb row)
    {
        return new StrategySummary(
            row.Id,
            row.Name,
            row.CurrentVersion,
            row.UpdatedAt,
            row.TemplateKind is null ? null : Enum.Parse<StrategyTemplateKind>(row.TemplateKind));
    }
}