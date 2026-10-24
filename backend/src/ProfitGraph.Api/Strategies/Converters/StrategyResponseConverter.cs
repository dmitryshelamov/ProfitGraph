using ProfitGraph.Api.Backtesting.Converters;
using ProfitGraph.Api.Strategies.Contracts;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Strategies.Contracts;

namespace ProfitGraph.Api.Strategies.Converters;

/// <summary>
///     Explicit JSON boundary: no object or JsonElement values reach Application or Domain.
/// </summary>
internal static class StrategyResponseConverter
{
    public static GraphLayoutInternal ToInternal(this GraphLayoutRequest layout)
    {
        if (layout.Positions is null || layout.Positions.Values.Any(position => position is null))
        {
            throw new ApplicationValidationException([new InputError("invalid_graph_payload", "layout", "Layout positions must not be null.")]);
        }

        return new GraphLayoutInternal(layout.Positions.ToDictionary(pair => pair.Key, pair => new NodePositionInternal(pair.Value.X, pair.Value.Y)));
    }

    private static GraphLayoutResponse ToResponse(this GraphLayoutInternal layout)
    {
        return new GraphLayoutResponse(layout.Positions.ToDictionary(pair => pair.Key, pair => new NodePositionResponse(pair.Value.X, pair.Value.Y)));
    }

    public static StrategyResponse ToResponse(this StrategyInternal strategy)
    {
        return new StrategyResponse(
            strategy.Id,
            strategy.Name,
            strategy.CurrentVersion,
            strategy.CreatedAt,
            strategy.UpdatedAt,
            strategy.Definition.ToResponse(),
            strategy.Layout.ToResponse());
    }

    public static StrategySummaryResponse ToResponse(this StrategySummaryInternal strategy)
    {
        return new StrategySummaryResponse(
            strategy.Id,
            strategy.Name,
            strategy.CurrentVersion,
            strategy.UpdatedAt,
            strategy.TemplateKind?.ToString(),
            strategy.LatestRun?.ToResponse());
    }

    public static StrategyVersionResponse ToResponse(this StrategyVersionInternal version)
    {
        return new StrategyVersionResponse(
            version.Id,
            version.StrategyId,
            version.Number,
            version.CreatedAt,
            version.Definition.ToResponse(),
            version.Layout.ToResponse());
    }

    public static StrategyVersionSummaryResponse ToResponse(this StrategyVersionSummaryInternal version)
    {
        return new StrategyVersionSummaryResponse(version.Id, version.Number, version.CreatedAt);
    }

    public static StrategyTemplateResponse ToResponse(this StrategyTemplateInternal template)
    {
        return new StrategyTemplateResponse(
            template.Kind.ToString(),
            template.Name,
            template.Description,
            template.Definition.ToResponse(),
            template.Layout.ToResponse());
    }
}