using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     Collects each eligible action once from the fixed dispatch projection, before sequential live application.
/// </summary>
public sealed class ActionCollector
{
    private readonly EngineParameterResolver _resolver = new EngineParameterResolver();

    public ActionCollectionResult Collect(
        CompiledTradingGraph graph,
        EventOccurrence occurrence,
        ReachedMarketContext context,
        IReadOnlyDictionary<PortReference, EngineValue> outputs,
        EngineExecutionInputs execution)
    {
        var requests = new List<ActionRequest>();
        var skips = new List<ActionSkipDiagnostic>();

        CollectInto(graph, occurrence, context, new SnapshotActionValueReader(outputs, execution, _resolver), requests, skips);

        return new ActionCollectionResult(requests, skips);
    }

    internal void CollectForReplay(
        CompiledTradingGraph graph,
        EventOccurrence occurrence,
        ReachedMarketContext context,
        EngineValue[] outputs,
        CandleValueEvaluator evaluator,
        List<ActionRequest> requests,
        List<ActionSkipDiagnostic> skips)
    {
        requests.Clear();
        skips.Clear();

        CollectInto(graph, occurrence, context, new ReplayActionValueReader(outputs, evaluator), requests, skips);
    }

    private static void CollectInto<TReader>(
        CompiledTradingGraph graph,
        EventOccurrence occurrence,
        ReachedMarketContext context,
        TReader reader,
        List<ActionRequest> requests,
        List<ActionSkipDiagnostic> skips)
        where TReader : struct, IActionValueReader
    {
        for (var nodeIndex = 0; nodeIndex < graph.ActionNodes.Count; nodeIndex++)
        {
            CompiledTradingNode node = graph.ActionNodes[nodeIndex];
            bool eligible;

            if (node.Node.EvaluationMode == ActionEvaluationMode.OnCandleClose)
            {
                eligible = occurrence.Kind == EventKind.CandleClosed && occurrence.Phase == PricePhase.Close;
            }
            else
            {
                EngineValue value = reader.ReadTrigger(node.TriggerBinding!);
                eligible = value.IsAvailable && value.Event.Id == occurrence.Id;
            }

            if (eligible)
            {
                CollectNode(node, occurrence, context, reader.ResolveParameters(node), requests, skips);
            }
        }
    }

    private static void CollectNode(
        CompiledTradingNode node,
        EventOccurrence occurrence,
        ReachedMarketContext context,
        ResolvedEngineParameters values,
        List<ActionRequest> requests,
        List<ActionSkipDiagnostic> skips)
    {
        EngineValue enabled = values["enabled"];

        if (!enabled.IsAvailable || !enabled.Boolean)
        {
            skips.Add(new ActionSkipDiagnostic(node.Node.Id, occurrence.Id, enabled.IsAvailable ? "Disabled" : "EnabledUnavailable"));

            return;
        }

        if (node.TradingBehavior == TradingNodeBehavior.CancelOrder)
        {
            EngineValue id = values["orderId"];
            EngineValue tag = values["tag"];

            if (!id.IsAvailable && !tag.IsAvailable)
            {
                skips.Add(new ActionSkipDiagnostic(node.Node.Id, occurrence.Id, "CancelTargetUnavailable"));

                return;
            }

            requests.Add(id.IsAvailable ? CancelActionRequest.ByOrderId(node.Node.Id, id.OrderId) : CancelActionRequest.ByTag(node.Node.Id, tag.Tag));

            return;
        }

        var requiredValueUnavailable = false;

        for (var inputIndex = 0; inputIndex < node.EngineInputs.Count; inputIndex++)
        {
            CompiledEngineInput input = node.EngineInputs[inputIndex];

            if (input.Spec.Required && !values[input.Spec.Key].IsAvailable)
            {
                requiredValueUnavailable = true;

                break;
            }
        }

        if (requiredValueUnavailable)
        {
            skips.Add(new ActionSkipDiagnostic(node.Node.Id, occurrence.Id, "RequiredValueUnavailable"));

            return;
        }

        bool buy = node.TradingBehavior is TradingNodeBehavior.MarketBuy or TradingNodeBehavior.LimitBuy;
        bool limit = node.TradingBehavior is TradingNodeBehavior.LimitBuy or TradingNodeBehavior.LimitSell;
        var order = new OrderRequest(
            buy ? OrderSide.Buy : OrderSide.Sell,
            limit ? OrderType.Limit : OrderType.Market,
            values[buy ? "amount" : "quantity"].Number,
            limit ? values["price"].Number : null,
            values["tag"].Tag,
            node.Node.Id,
            context.EligibleFromCandleIndex,
            buy && values.TextSettings.GetValueOrDefault("minimumOrderPolicy") == "RaiseToExchangeMinimum"
                ? MinimumOrderPolicy.RaiseToExchangeMinimum
                : MinimumOrderPolicy.Reject);
        requests.Add(new SubmitActionRequest(order));
    }
}