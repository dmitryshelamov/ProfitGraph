using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Grids;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Nodes.BuiltIn;

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

            if (node.TradingBehavior == TradingNodeBehavior.Grid)
            {
                CollectGrid(node, occurrence, reader, requests, skips);

                continue;
            }

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

    /// <summary>
    ///     A grid starts when an event reaches it and reacts to its own orders, its start, every candle close and every resume
    ///     (ADR 0058, section 4); its settings are read again each time.
    /// </summary>
    private static void CollectGrid<TReader>(
        CompiledTradingNode node,
        EventOccurrence occurrence,
        TReader reader,
        List<ActionRequest> requests,
        List<ActionSkipDiagnostic> skips)
        where TReader : struct, IActionValueReader
    {
        EngineValue trigger = reader.ReadTrigger(node.TriggerBinding!);
        bool triggered = trigger.IsAvailable && trigger.Event.Id == occurrence.Id;
        bool reacts = ReactsToGrid(occurrence, node.Node.Id);

        if (!triggered && !reacts)
        {
            return;
        }

        GridSettings? settings = Settings(reader.ResolveParameters(node));

        if (triggered)
        {
            if (settings is null)
            {
                skips.Add(new ActionSkipDiagnostic(node.Node.Id, occurrence.Id, "InvalidGridSettings"));
            }
            else
            {
                requests.Add(new GridStartRequest(node.Node.Id, settings));
            }
        }

        if (reacts)
        {
            requests.Add(new GridEvaluateRequest(node.Node.Id, settings));
        }
    }

    private static bool ReactsToGrid(EventOccurrence occurrence, string gridNodeId)
    {
        return occurrence.Kind switch
        {
            EventKind.CandleClosed => occurrence.Phase == PricePhase.Close,
            EventKind.Resume => true,
            EventKind.GridStarted => occurrence.Payload is GridPayload grid && grid.OriginNodeId == gridNodeId,
            EventKind.Filled or EventKind.Cancelled or EventKind.Rejected => occurrence.Payload switch
            {
                FillPayload fill => fill.OriginNodeId == gridNodeId,
                OrderPayload order => order.Order.OriginNodeId == gridNodeId,
                RejectionPayload rejection => rejection.Rejection.OriginNodeId == gridNodeId,
                _ => false
            },
            _ => false
        };
    }

    private static GridSettings? Settings(ResolvedEngineParameters values)
    {
        foreach ((string key, _) in GridNodeDefinition.Settings)
        {
            if (!values[key].IsAvailable)
            {
                return null;
            }
        }

        return GridSettings.Create(
            values["levels"].Number,
            values["step"].Number,
            values["profitStep"].Number,
            values["lot"].Number,
            values["maxOpenBuys"].Number,
            values["shiftThreshold"].Number,
            values["anchorCeiling"].Number,
            values["stopBelow"].Number);
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

        if (node.TradingBehavior == TradingNodeBehavior.CancelGrid)
        {
            requests.Add(new GridCancelRequest(node.Node.Id, values.TextSettings.GetValueOrDefault("grid", string.Empty)));

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

        bool cover = node.TradingBehavior is TradingNodeBehavior.MarketCover or TradingNodeBehavior.LimitCover;
        bool buy = cover || node.TradingBehavior is TradingNodeBehavior.MarketBuy or TradingNodeBehavior.LimitBuy;
        bool limit = node.TradingBehavior is TradingNodeBehavior.LimitBuy or TradingNodeBehavior.LimitSell or TradingNodeBehavior.LimitCover;
        var order = new OrderRequest(
            buy ? OrderSide.Buy : OrderSide.Sell,
            limit ? OrderType.Limit : OrderType.Market,
            values[buy && !cover ? "amount" : "quantity"].Number,
            limit ? values["price"].Number : null,
            values["tag"].Tag,
            node.Node.Id,
            context.EligibleFromCandleIndex,
            buy && values.TextSettings.GetValueOrDefault("minimumOrderPolicy") == "RaiseToExchangeMinimum"
                ? MinimumOrderPolicy.RaiseToExchangeMinimum
                : MinimumOrderPolicy.Reject,
            limit ? null : context.MarketPrice);
        requests.Add(new SubmitActionRequest(order));
    }
}