using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;

namespace ProfitGraph.Domain.Optimization;

/// <summary>
///     The parameter relations a strategy template relies on, such as a fast SMA shorter than the slow one, checked
///     against static values before a candidate is replayed (Profit Fox Designer ADR 0033). A parameter the strategy no
///     longer has only skips its rule, so editing a template never makes it unrunnable; a present one must be positive.
/// </summary>
public static class TemplateParameterRules
{
    public static IReadOnlyList<GraphDiagnostic> Check(StrategyTemplateKind? template, CompiledTradingGraph graph, EngineExecutionInputs selections)
    {
        if (template is null)
        {
            return [];
        }

        GraphValidationResult selection = new EngineParameterResolver().ValidateSelections(graph, selections);

        if (!selection.IsValid)
        {
            return selection.Diagnostics;
        }

        IReadOnlyDictionary<PortReference, EngineValue> values;

        try
        {
            values = new CandleValueEvaluator(graph, selections).ReadStaticValues();
        }
        catch (GraphExecutionException exception)
        {
            return [new GraphDiagnostic(GraphDiagnosticCode.InvalidParameter, exception.Message, exception.NodeId)];
        }

        return new TemplateParameterCheck(graph, values).Run(template.Value);
    }
}