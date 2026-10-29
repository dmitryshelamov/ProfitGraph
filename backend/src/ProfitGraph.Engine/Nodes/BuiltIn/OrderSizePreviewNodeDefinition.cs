using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Metadata and behavior for the OrderSizePreview node.
/// </summary>
public sealed class OrderSizePreviewNodeDefinition : INodeDefinition
{
    public string TypeKey => nameof(TradingNodeBehavior.OrderSizePreview);
    public bool IsAction => false;
    public TradingNodeBehavior? TradingBehavior => TradingNodeBehavior.OrderSizePreview;
    public NodeClock? EngineClock => NodeClock.Dispatch;
    public NodeCategory Category => NodeCategory.Risk;
    public int SchemaVersion => 1;

    public IReadOnlyList<PortSpec> Inputs { get; } = Array.AsReadOnly(
        new[]
        {
            new PortSpec("amount", PortKind.Series, "amount", ValueType.QuoteAmount),
            new PortSpec("quantity", PortKind.Series, "quantity", ValueType.BaseAmount),
            new PortSpec("price", PortKind.Series, "price", ValueType.Price),
            new PortSpec("quoteLimit", PortKind.Series, "quoteLimit", ValueType.QuoteAmount)
        });

    public IReadOnlyList<PortSpec> Outputs { get; } = Array.AsReadOnly(
        new[]
        {
            new PortSpec("valid", PortKind.Condition, "valid", ValueType.Boolean),
            new PortSpec("normalizedQuantity", PortKind.Series, "normalizedQuantity", ValueType.BaseAmount),
            new PortSpec("requiredQuote", PortKind.Series, "requiredQuote", ValueType.QuoteAmount),
            new PortSpec("requiredBase", PortKind.Series, "requiredBase", ValueType.BaseAmount),
            new PortSpec("reason", PortKind.Series, "reason", ValueType.RejectionReason)
        });

    public IReadOnlyList<ParamSpec> Params { get; } = Array.AsReadOnly(
        new[]
        {
            new ParamSpec(
                "minimumOrderPolicy",
                "minimumOrderPolicy",
                ParamValueKind.Text,
                "Reject",
                AllowedValues: Array.AsReadOnly(new[] { "Reject", "RaiseToExchangeMinimum" })),
            new ParamSpec("side", "side", ParamValueKind.Text, "Buy", AllowedValues: Array.AsReadOnly(new[] { "Buy", "Sell" })),
            new ParamSpec("type", "type", ParamValueKind.Text, "Market", AllowedValues: Array.AsReadOnly(new[] { "Market", "Limit" }))
        });

    public IReadOnlyList<EngineInputSpec> EngineInputs { get; } = Array.AsReadOnly(
        new[]
        {
            new EngineInputSpec("amount", ValueType.QuoteAmount, required: false, supportsInline: true),
            new EngineInputSpec("quantity", ValueType.BaseAmount, required: false, supportsInline: true),
            new EngineInputSpec("price", ValueType.Price, required: false, supportsInline: true),
            new EngineInputSpec("quoteLimit", ValueType.QuoteAmount, required: false, supportsInline: true)
        });

    public IReadOnlyList<ActionEvaluationMode> AllowedEvaluationModes { get; } = Array.Empty<ActionEvaluationMode>();

    public bool ProjectsTradingState => true;

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return NodeClock.Dispatch;
    }

    public void EvaluateTrading(NodeTradingEvaluationContext context)
    {
        ResolvedEngineParameters parameters = context.Parameters;
        OrderSide selectedSide = ParseSide(parameters.TextSettings["side"]);
        OrderType selectedType = ParseType(parameters.TextSettings["type"]);
        ExecutionSettings settings = context.Dispatch.Settings;

        // A short's buy covers a base quantity (ADR 0061).
        EngineValue amount = parameters[selectedSide == OrderSide.Buy && settings.Direction == PositionDirection.Long ? "amount" : "quantity"];
        EngineValue price = parameters["price"];

        if (!amount.IsAvailable || (selectedType == OrderType.Limit && !price.IsAvailable))
        {
            return;
        }

        var hasQuoteLimit = false;

        for (var index = 0; index < context.Node.EngineInputs.Count; index++)
        {
            CompiledEngineInput input = context.Node.EngineInputs[index];

            if (input.Spec.Key == "quoteLimit" && (input.Source is not null || input.Inline is not null))
            {
                hasQuoteLimit = true;

                break;
            }
        }

        bool quoteLimitAvailable = parameters.Values.TryGetValue("quoteLimit", out EngineValue quoteLimit) && quoteLimit.IsAvailable;

        if (hasQuoteLimit && !quoteLimitAvailable)
        {
            return;
        }

        OrderSizingResult sizing = context.Sizing.ResolvePreview(
            selectedSide,
            selectedType,
            amount.Number,
            selectedType == OrderType.Limit ? price.Number : null,
            context.Dispatch.Rules,
            context.Dispatch.Settings,
            context.Dispatch.Account,
            context.Dispatch.Market.MarketPrice,
            parameters.TextSettings.GetValueOrDefault("minimumOrderPolicy") == "RaiseToExchangeMinimum"
                ? MinimumOrderPolicy.RaiseToExchangeMinimum
                : MinimumOrderPolicy.Reject,
            quoteLimitAvailable ? quoteLimit.Number : null);
        context.Values["valid"] = EngineValue.FromBoolean(sizing.IsValid);

        if (sizing.Quantity.HasValue)
        {
            context.Number("normalizedQuantity", sizing.Quantity.Value);
        }

        context.Number(selectedSide == settings.OpeningSide ? "requiredQuote" : "requiredBase", sizing.Reservation);

        if (sizing.Reason.HasValue)
        {
            context.Values["reason"] = EngineValue.FromRejectionReason(sizing.Reason.Value);
        }
    }

    private static OrderSide ParseSide(string side)
    {
        return side switch
        {
            nameof(OrderSide.Buy) => OrderSide.Buy,
            nameof(OrderSide.Sell) => OrderSide.Sell,
            _ => throw new InvalidOperationException($"Unsupported order side '{side}'.")
        };
    }

    private static OrderType ParseType(string type)
    {
        return type switch
        {
            nameof(OrderType.Market) => OrderType.Market,
            nameof(OrderType.Limit) => OrderType.Limit,
            _ => throw new InvalidOperationException($"Unsupported order type '{type}'.")
        };
    }
}