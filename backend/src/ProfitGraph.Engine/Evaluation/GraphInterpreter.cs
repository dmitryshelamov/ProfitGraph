using System.Collections.ObjectModel;

using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Evaluation;

/// <summary>
///     Выполняет ациклический вычислительный граф по свечам. Не создаёт ордера и не ведёт счёт.
/// </summary>
public sealed class GraphInterpreter
{
    public IReadOnlyList<GraphBarResult> Run(
        CompiledGraph graph,
        IReadOnlyList<Candle> candles,
        string symbol,
        string timeframe,
        GraphExecutionInputs? execution = null,
        CancellationToken cancellationToken = default)
    {
        var state = new GraphExecutionState();
        var results = new List<GraphBarResult>();
        var resolver = new GraphParamResolver();

        for (var bar = 0; bar < candles.Count; bar++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outputs = new Dictionary<PortReference, NodeOutput>();

            foreach (CompiledNode compiled in graph.Nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Dictionary<string, NodeOutput> inputs = compiled.Inputs.ToDictionary(
                    binding => binding.InputKey,
                    binding => outputs[binding.Source],
                    StringComparer.Ordinal);
                Dictionary<string, IReadOnlyList<NodeOutput>> history = compiled.Inputs.ToDictionary(
                    binding => binding.InputKey,
                    binding => state.History(binding.Source),
                    StringComparer.Ordinal);
                var context = new NodeContext(bar, candles, symbol, timeframe, history);
                IReadOnlyDictionary<string, NodeOutput>? evaluated;

                try
                {
                    ResolvedParams? parameters = resolver.Resolve(compiled, outputs, execution ?? GraphExecutionInputs.Empty);
                    evaluated = parameters is not null &&
                        (compiled.Definition.AcceptsUnavailableInputs || inputs.Values.All(value => value.IsAvailable))
                            ? compiled.Definition.Evaluate(context, inputs, parameters)
                            : null;
                }
                catch (Exception exception) when (exception is ParameterResolutionException or InvalidOperationException or ArithmeticException)
                {
                    throw new GraphExecutionException(compiled.Node.Id, bar, exception.Message);
                }

                foreach (PortSpec port in compiled.Definition.Outputs)
                {
                    if (evaluated is not null && !evaluated.ContainsKey(port.Key))
                    {
                        throw new GraphExecutionException(compiled.Node.Id, bar, $"Missing output '{port.Key}'.");
                    }

                    NodeOutput output = evaluated is null ? NodeOutput.Unavailable(port.Kind) : evaluated[port.Key];

                    if (output.Kind != port.Kind || (output.IsAvailable && output.Kind == PortKind.Series &&
                            !double.IsFinite(output.SeriesValue)))
                    {
                        throw new GraphExecutionException(compiled.Node.Id, bar, $"Invalid output '{port.Key}'.");
                    }

                    var reference = new PortReference(compiled.Node.Id, port.Key);
                    outputs.Add(reference, output);
                    state.Append(reference, output);
                }
            }

            results.Add(new GraphBarResult(bar, new ReadOnlyDictionary<PortReference, NodeOutput>(outputs)));
        }

        return results.AsReadOnly();
    }
}