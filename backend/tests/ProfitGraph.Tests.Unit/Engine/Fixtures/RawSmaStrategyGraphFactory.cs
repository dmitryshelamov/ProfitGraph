using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Graphs;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Fixtures;

public static class RawSmaStrategyGraphFactory
{
    public static StrategyGraph Create(
        int fastPeriod = 20,
        int slowPeriod = 50,
        decimal buyAmount = 100m)
    {
        var nodes = new List<GraphNode>();
        var wires = new List<GraphConnection>();
        Parameter("fastPeriod", ValueType.Integer, fastPeriod);
        Parameter("slowPeriod", ValueType.Integer, slowPeriod);
        Parameter("buyAmount", ValueType.QuoteAmount, buyAmount);
        Node("close", "CandleField", new Dictionary<string, ParamValue> { ["field"] = Text("Close") });
        Node("fast", "SMA", new Dictionary<string, ParamValue> { ["period"] = new WiredParamValue(new PortReference("fastPeriod", "out")) });
        Node("slow", "SMA", new Dictionary<string, ParamValue> { ["period"] = new WiredParamValue(new PortReference("slowPeriod", "out")) });
        Wire("close", "fast", "in");
        Wire("close", "slow", "in");

        foreach (string direction in new[] { "up", "down" })
        {
            Node(direction, direction == "up" ? "CrossesAbove" : "CrossesBelow");
            Wire("fast", direction, "a");
            Wire("slow", direction, "b");
        }

        Node("balance", "Balance");
        Node("position", "Position");
        Node("active", "HasActiveOrders");
        Node("noActive", "Not");
        Wire("active", "noActive", "in");
        Node("sellPreview", "OrderSizePreview", new Dictionary<string, ParamValue> { ["side"] = Text("Sell"), ["type"] = Text("Market") });
        Wire("balance.baseAvailable", "sellPreview", "quantity");
        Node("zeroBase", "Constant", new Dictionary<string, ParamValue> { ["value"] = Number(0m) }, ValueType.BaseAmount);
        Compare("zeroPosition", "position.quantity", "zeroBase");

        foreach (RejectionReason reason in new[] { RejectionReason.ZeroQuantity, RejectionReason.BelowMinQuantity, RejectionReason.BelowMinNotional })
        {
            Node(
                reason.ToString(),
                "Constant",
                new Dictionary<string, ParamValue> { ["value"] = new FixedParamValue(new RejectionReasonParamLiteral(reason)) },
                ValueType.RejectionReason);
            Compare("is" + reason, "sellPreview.reason", reason.ToString());
        }

        Binary("dustFirst", "Or", "isZeroQuantity", "isBelowMinQuantity");
        Binary("dust", "Or", "dustFirst", "isBelowMinNotional");
        Binary("flat", "Or", "zeroPosition", "dust");
        Binary("buyCrossFlat", "And", "up", "flat");
        Binary("buyEnabled", "And", "buyCrossFlat", "noActive");
        Node("positiveBase", "Compare", new Dictionary<string, ParamValue> { ["op"] = Text(">") });
        Wire("balance.baseAvailable", "positiveBase", "a");
        Wire("zeroBase", "positiveBase", "b");
        Binary("sellCrossReady", "And", "down", "positiveBase");
        Binary("sellEnabled", "And", "sellCrossReady", "noActive");
        Node(
            "buy",
            "MarketBuy",
            new Dictionary<string, ParamValue>
            {
                ["tag"] = Text("sma.entry"),
                ["amount"] = new WiredParamValue(new PortReference("buyAmount", "out"))
            },
            mode: ActionEvaluationMode.OnCandleClose);
        Node("sell", "MarketSell", new Dictionary<string, ParamValue> { ["tag"] = Text("sma.exit") }, mode: ActionEvaluationMode.OnCandleClose);
        Wire("buyEnabled", "buy", "enabled");
        Wire("sellEnabled", "sell", "enabled");
        Wire("balance.baseAvailable", "sell", "quantity");

        return new StrategyGraph(nodes, wires, StrategyTemplateKind.SmaCrossover);

        void Node(string id, string type, Dictionary<string, ParamValue>? parameters = null, ValueType? declared = null, ActionEvaluationMode? mode = null)
        {
            int schema = type is "Parameter" or "Constant" or "CandleField" or "SMA" or "CrossesAbove" or "CrossesBelow" or "Compare" ? 2 : 1;
            nodes.Add(new GraphNode(id, type, schema, parameters ?? new Dictionary<string, ParamValue>(), declared, mode));
        }

        void Parameter(string id, ValueType type, decimal value)
        {
            Node(id, "Parameter", new Dictionary<string, ParamValue> { ["name"] = Text(id), ["value"] = Number(value) }, type);
        }

        void Wire(string source, string target, string input)
        {
            string[] pieces = source.Split('.');
            wires.Add(new GraphConnection(new PortReference(pieces[0], pieces.Length == 1 ? "out" : pieces[1]), new PortReference(target, input)));
        }

        void Binary(string id, string type, string a, string b)
        {
            Node(id, type);
            Wire(a, id, "a");
            Wire(b, id, "b");
        }

        void Compare(string id, string a, string b)
        {
            Node(id, "Compare", new Dictionary<string, ParamValue> { ["op"] = Text("=") });
            Wire(a, id, "a");
            Wire(b, id, "b");
        }
    }

    private static FixedParamValue Number(decimal value)
    {
        return new FixedParamValue(new DecimalParamLiteral(value));
    }

    private static FixedParamValue Text(string value)
    {
        return new FixedParamValue(new TextParamLiteral(value));
    }
}