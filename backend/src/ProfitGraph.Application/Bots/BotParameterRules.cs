using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Engine.Blocks;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Application.Bots;

/// <summary>
///     A bot gives a value for every parameter of its strategy version and nothing else; an integer parameter takes a
///     whole number (Profit Fox Designer ADR 0068).
/// </summary>
internal static class BotParameterRules
{
    private static IReadOnlyList<InputError> Check(StrategyDefinition definition, IReadOnlyDictionary<string, decimal> values)
    {
        var errors = new List<InputError>();

        foreach (StrategyParameter parameter in definition.Parameters)
        {
            if (!values.TryGetValue(parameter.Name, out decimal value))
            {
                errors.Add(new InputError(BotErrorCodes.ParametersMismatch, $"Parameters.{parameter.Name}", "The bot needs a value for this parameter."));
            }
            else if (parameter.ValueType == ValueType.Integer && decimal.Truncate(value) != value)
            {
                errors.Add(new InputError(BotErrorCodes.ParametersMismatch, $"Parameters.{parameter.Name}", "This parameter takes a whole number."));
            }
        }

        errors.AddRange(
            values.Keys.Where(name => definition.Parameters.All(parameter => parameter.Name != name))
                .Select(name => new InputError(BotErrorCodes.ParametersMismatch, $"Parameters.{name}", "The strategy has no such parameter.")));

        return errors;
    }

    internal static void Ensure(StrategyDefinition definition, IReadOnlyDictionary<string, decimal> values)
    {
        IReadOnlyList<InputError> errors = Check(definition, values);

        if (errors.Count > 0)
        {
            throw new ApplicationValidationException(errors);
        }
    }
}