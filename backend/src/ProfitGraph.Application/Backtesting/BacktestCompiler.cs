using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Common.Ports;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Application.Backtesting;

/// <summary>
///     Turns a saved strategy definition with parameter values into the plan the engine replays. Starting a run and
///     executing it compile the same way, so the fingerprint frozen at the start is checked at execution.
/// </summary>
public sealed class BacktestCompiler(INodeTypeRegistry registry)
{
    /// <param name="definition">The saved strategy definition.</param>
    /// <param name="parameters">Fixed parameter values by name that replace the saved defaults.</param>
    /// <param name="ranges">
    ///     Optimization ranges by parameter name; a parameter with a range is a search dimension instead of a fixed value
    ///     (Profit Fox Designer ADR 0038: ranges belong to the request, never to the saved version).
    /// </param>
    public CompiledTradingGraph Compile(
        StrategyDefinition definition,
        IReadOnlyDictionary<string, decimal> parameters,
        IReadOnlyDictionary<string, DecimalOptimizeParamValue>? ranges = null)
    {
        IReadOnlyList<INodeDefinition> catalog = registry.GetAll();
        StrategyDefinition applied = Apply(definition, parameters);
        CheckRanges(definition, parameters, ranges);
        StrategyBlockValidationResult validation = new StrategyDefinitionValidator().Validate(applied, catalog);

        if (!validation.IsValid)
        {
            throw new ApplicationValidationException(
            [
                .. validation.Diagnostics.Select(diagnostic => new InputError(
                    BacktestErrorCodes.StrategyInvalid,
                    diagnostic.ParameterName is null ? "Strategy" : $"Parameters.{diagnostic.ParameterName}",
                    diagnostic.Message))
            ]);
        }

        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(new StrategyDefinitionLowerer(catalog).Lower(applied, ranges).Graph, catalog);

        return compilation.Graph ?? throw new ApplicationValidationException(
        [
            .. compilation.Validation.Diagnostics.Select(diagnostic => new InputError(BacktestErrorCodes.StrategyInvalid, "Strategy", diagnostic.Message))
        ]);
    }

    private static void CheckRanges(
        StrategyDefinition definition,
        IReadOnlyDictionary<string, decimal> parameters,
        IReadOnlyDictionary<string, DecimalOptimizeParamValue>? ranges)
    {
        if (ranges is null)
        {
            return;
        }

        InputError[] errors =
        [
            .. ranges.Keys.Where(name => definition.Parameters.All(parameter => parameter.Name != name))
                .Select(name => new InputError(BacktestErrorCodes.UnknownParameter, $"Ranges.{name}", "The strategy has no such parameter.")),
            .. ranges.Keys.Where(parameters.ContainsKey)
                .Select(name => new InputError(BacktestErrorCodes.ParameterFixedAndRanged, $"Ranges.{name}", "A parameter is either fixed or optimized, not both."))
        ];

        if (errors.Length > 0)
        {
            throw new ApplicationValidationException(errors);
        }
    }

    private static StrategyDefinition Apply(StrategyDefinition definition, IReadOnlyDictionary<string, decimal> parameters)
    {
        string[] unknown = [.. parameters.Keys.Where(name => definition.Parameters.All(parameter => parameter.Name != name))];

        if (unknown.Length > 0)
        {
            throw new ApplicationValidationException(
            [
                .. unknown.Select(name => new InputError(BacktestErrorCodes.UnknownParameter, $"Parameters.{name}", "The strategy has no such parameter."))
            ]);
        }

        return new StrategyDefinition(
            definition.Parameters.Select(parameter => parameters.TryGetValue(parameter.Name, out decimal value) ? parameter with { DefaultValue = value } : parameter),
            definition.Values,
            definition.Blocks,
            definition.Wires,
            definition.TemplateKind);
    }
}