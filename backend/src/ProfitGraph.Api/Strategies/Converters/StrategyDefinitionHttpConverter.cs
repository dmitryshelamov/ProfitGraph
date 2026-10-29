using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Api.Strategies.Contracts;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Api.Strategies.Converters;

internal static class StrategyDefinitionHttpConverter
{
    public static StrategyDefinitionInternal ToInternal(this StrategyDefinitionRequest definition)
    {
        if (definition.Parameters is null || definition.Values is null || definition.Blocks is null || definition.Wires is null ||
            definition.Parameters.Any(item => item is null) || definition.Values.Any(item => item is null || item.Expression is null) ||
            definition.Blocks.Any(item => item is null) || definition.Wires.Any(item => item is null || item.From is null))
        {
            throw Invalid("definition", "Definition collections and their elements must not be null.");
        }

        return new StrategyDefinitionInternal(
            definition.Parameters.Select(item => new StrategyParameterInternal(
                    item.Name,
                    ParseEnum<ValueType>(item.ValueType, "valueType"),
                    ParseNumber(item.DefaultValue),
                    ParseDisplayUnit(item.DisplayUnit)))
                .ToArray(),
            definition.Values.Select(item => new StrategyValueInternal(item.Name, item.Expression.ToInternal())).ToArray(),
            definition.Blocks.Select(ToInternal).ToArray(),
            definition.Wires.Select(item => new StrategyWireInternal(new StrategyExitInternal(item.From.Block, item.From.Exit), item.To)).ToArray(),
            definition.TemplateKind is null ? null : ParseEnum<StrategyTemplateKind>(definition.TemplateKind, "templateKind"),
            definition.Direction is null ? PositionDirection.Long : ParseEnum<PositionDirection>(definition.Direction, "direction"));
    }

    public static StrategyDefinitionResponse ToResponse(this StrategyDefinitionInternal definition)
    {
        return new StrategyDefinitionResponse(
            definition.Parameters.Select(item => new StrategyParameterResponse(
                    item.Name,
                    item.ValueType.ToString(),
                    ExactGraphNumberParser.Format(item.DefaultValue),
                    item.DisplayUnit?.ToString().ToLowerInvariant()))
                .ToArray(),
            definition.Values.Select(item => new StrategyValueResponse(item.Name, item.Expression.ToResponse())).ToArray(),
            definition.Blocks.Select(ToResponse).ToArray(),
            definition.Wires.Select(item => new StrategyWireResponse(new StrategyExitResponse(item.From.Block, item.From.Exit), item.To)).ToArray(),
            definition.TemplateKind?.ToString(),
            definition.Direction == PositionDirection.Short ? nameof(PositionDirection.Short) : null);
    }

    private static StrategyBlockInternal ToInternal(StrategyBlockRequest block)
    {
        if (block.Checks?.Any(check => check is null) == true)
        {
            throw Invalid("definition.blocks.checks", "Checks must not contain null elements.");
        }

        return new StrategyBlockInternal(
            block.Id,
            block.Kind,
            block.DisplayName,
            block.Source,
            block.Join,
            block.Checks?.Select(check => new StrategyCheckInternal(
                    check.Type,
                    check.Operation,
                    ConvertArguments(check.Operands),
                    check.Tag?.ToInternal(),
                    check.Side,
                    check.OrderKind,
                    check.SizeMode,
                    check.MinimumOrderPolicy == "Reject" ? null : check.MinimumOrderPolicy))
                .ToArray(),
            block.OrderKind,
            block.Side,
            block.Tag?.ToInternal(),
            block.Amount?.ToInternal(),
            block.Quantity?.ToInternal(),
            block.Price?.ToInternal(),
            block.SizeMode,
            block.MinimumOrderPolicy == "Reject" ? null : block.MinimumOrderPolicy,
            block.Settings is null ? null : ConvertArguments(block.Settings),
            block.Grid);
    }

    private static StrategyBlockResponse ToResponse(StrategyBlockInternal block)
    {
        return new StrategyBlockResponse(
            block.Id,
            block.Kind,
            block.DisplayName,
            block.Source,
            block.Join,
            block.Checks?.Select(check => new StrategyCheckResponse(
                    check.Type,
                    check.Operation,
                    check.Operands?.ToDictionary(pair => pair.Key, pair => pair.Value.ToResponse()),
                    check.Tag?.ToResponse(),
                    check.Side,
                    check.OrderKind,
                    check.SizeMode,
                    check.MinimumOrderPolicy == "Reject" ? null : check.MinimumOrderPolicy))
                .ToArray(),
            block.OrderKind,
            block.Side,
            block.Tag?.ToResponse(),
            block.Amount?.ToResponse(),
            block.Quantity?.ToResponse(),
            block.Price?.ToResponse(),
            block.SizeMode,
            block.MinimumOrderPolicy == "Reject" ? null : block.MinimumOrderPolicy,
            block.Settings?.ToDictionary(pair => pair.Key, pair => pair.Value.ToResponse(), StringComparer.Ordinal),
            block.Grid);
    }

    private static ValueExpressionInternal ToInternal(this ValueExpressionRequest expression)
    {
        return new ValueExpressionInternal(
            expression.Kind,
            expression.ValueType is null ? null : ParseEnum<ValueType>(expression.ValueType, "valueType"),
            expression.Number is null ? null : ParseNumber(expression.Number),
            expression.Text,
            expression.Name,
            expression.Field,
            expression.Operation,
            ConvertArguments(expression.Arguments));
    }

    private static ValueExpressionResponse ToResponse(this ValueExpressionInternal expression)
    {
        return new ValueExpressionResponse(
            expression.Kind,
            expression.ValueType?.ToString(),
            expression.Number is null ? null : ExactGraphNumberParser.Format(expression.Number.Value),
            expression.Text,
            expression.Name,
            expression.Field,
            expression.Operation,
            expression.Arguments?.ToDictionary(pair => pair.Key, pair => pair.Value.ToResponse()));
    }

    private static IReadOnlyDictionary<string, ValueExpressionInternal>? ConvertArguments(IReadOnlyDictionary<string, ValueExpressionRequest>? arguments)
    {
        if (arguments?.Values.Any(value => value is null) == true)
        {
            throw Invalid("definition.expression", "Expression operands must not be null.");
        }

        return arguments?.ToDictionary(pair => pair.Key, pair => pair.Value.ToInternal());
    }

    private static T ParseEnum<T>(string value, string field) where T : struct, Enum
    {
        if (!Enum.TryParse(value, out T parsed) || !Enum.IsDefined(parsed) || parsed.ToString() != value)
        {
            throw Invalid(field, $"Unknown {field} '{value}'.");
        }

        return parsed;
    }

    private static StrategyParameterDisplayUnit? ParseDisplayUnit(string? value)
    {
        return value switch
        {
            null => null,
            "none" => StrategyParameterDisplayUnit.None,
            "percent" => StrategyParameterDisplayUnit.Percent,
            "quote" => StrategyParameterDisplayUnit.Quote,
            "base" => StrategyParameterDisplayUnit.Base,
            _ => throw Invalid("definition.parameters.displayUnit", "Unknown parameter display unit.")
        };
    }

    private static decimal ParseNumber(string value)
    {
        if (!ExactGraphNumberParser.TryDecimal(value, out decimal number))
        {
            throw Invalid("definition.number", "Numbers must be exact representable invariant decimal strings.");
        }

        return number;
    }

    private static ApplicationValidationException Invalid(string field, string message)
    {
        return new ApplicationValidationException([new InputError("invalid_definition_payload", field, message)]);
    }
}