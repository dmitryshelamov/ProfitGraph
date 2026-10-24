using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Graphs;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Fixtures;

/// <summary>
///     Editable public primitive-node Ladder, including recovery, protective cancellation and explicit dust reasons.
/// </summary>
public static class RawLadderStrategyGraphFactory
{
    public static StrategyGraph Create(
        decimal buyAmount = 100m,
        decimal allocationCap = 300m,
        decimal drop = 0.1m,
        decimal rise = 0.1m)
    {
        var nodes = new List<GraphNode>();
        var wires = new List<GraphConnection>();
        Parameter("buyAmount", ValueType.QuoteAmount, buyAmount);
        Parameter("allocationCap", ValueType.QuoteAmount, allocationCap);
        Parameter("drop", ValueType.Scalar, drop);
        Parameter("rise", ValueType.Scalar, rise);
        Constant("one", ValueType.Scalar, value: 1m);
        Constant("zeroBase", ValueType.BaseAmount, value: 0m);
        Node("balance", "Balance");
        Node("position", "Position");
        Node("start", "OnStart");
        Node("close", "OnCandleClosed");

        foreach (string tag in new[] { "entry", "average", "exit" })
        {
            Node("active" + tag, "ActiveOrder", new Dictionary<string, ParamValue> { ["tag"] = Text("ladder." + tag) });
            Unary("no" + tag, "Not", "active" + tag + ".exists");
        }

        Binary("dropFactor", "Arithmetic", "Subtract", "one", "drop");
        Binary("riseFactor", "Arithmetic", "Add", "one", "rise");
        Binary("nextPrice", "Arithmetic", "Multiply", "position.lastBuyPrice", "dropFactor");
        Binary("exitPrice", "Arithmetic", "Multiply", "position.averageCost", "riseFactor");
        Binary("remaining", "Arithmetic", "Subtract", "allocationCap", "position.costBasis");
        Binary("allocationEnough", "Compare", ">=", "remaining", "buyAmount");
        Binary("cashEnough", "Compare", ">=", "balance.quoteAvailable", "buyAmount");
        Preview("wholePreview", "Sell", "Market", "position.quantity", price: null);
        Preview("residualPreview", "Sell", "Market", "balance.baseAvailable", price: null);
        Preview("exitPreview", "Sell", "Limit", "balance.baseAvailable", "exitPrice");
        Preview("averagePreview", "Buy", "Limit", "buyAmount", "nextPrice");
        Flat("flat", "wholePreview", "position.quantity");
        Flat("residualFlat", "residualPreview", "balance.baseAvailable");
        And("canAverage", "noaverage", "noentry", "allocationEnough", "cashEnough", "averagePreview.valid");
        And("canEnter", "noentry", "noaverage", "noexit", "flat", "allocationEnough", "cashEnough");
        And("residualRestart", "residualFlat", "canEnter");
        And("recoverExit", "wholePreview.valid", "noexit", "noentry");
        And("recoverAverage", "activeexit.exists", "canAverage");
        Filter("recoverExitEvent", "close", "recoverExit");
        Filter("recoverAverageEvent", "close", "recoverAverage");
        Filter("recoverEntryEvent", "close", "canEnter");
        Filter("restartEvent", "cancelAverage.completed", "residualRestart");
        Filter("residualEvent", "cancelAverage.completed", "residualPreview.valid");
        Merge("entryNormal", "start", "restartEvent");
        Merge("entryTriggers", "entryNormal", "recoverEntryEvent");
        Merge("buyFills", "entry.filled", "average.filled");
        Merge("exitTriggers", "buyFills", "recoverExitEvent");
        Merge("averageTriggers", "exit.submitted", "recoverAverageEvent");
        Merge("exitFills", "exit.filled", "residual.filled");
        Merge("sellRejections", "exit.rejected", "residual.rejected");
        Action("entry", "MarketBuy", "entryTriggers", "canEnter", "ladder.entry", "amount", "buyAmount", price: null);
        Cancel("cancelExit", "exitTriggers", "ladder.exit");
        Action("exit", "LimitSell", "cancelExit.completed", "exitPreview.valid", "ladder.exit", "quantity", "balance.baseAvailable", "exitPrice");
        Action("average", "LimitBuy", "averageTriggers", "canAverage", "ladder.average", "amount", "buyAmount", "nextPrice");
        Cancel("cancelAverage", "exitFills", "ladder.average");
        Action("residual", "MarketSell", "residualEvent", "residualPreview.valid", "ladder.exit", "quantity", "balance.baseAvailable", price: null);
        Cancel("protectiveCancel", "sellRejections", "ladder.average");

        return new StrategyGraph(nodes, wires, StrategyTemplateKind.Ladder);

        void Node(string id, string type, Dictionary<string, ParamValue>? parameters = null, ValueType? declared = null, ActionEvaluationMode? mode = null)
        {
            int schema = type is "Parameter" or "Constant" or "Arithmetic" or "Compare" ? 2 : 1;
            nodes.Add(new GraphNode(id, type, schema, parameters ?? new Dictionary<string, ParamValue>(), declared, mode));
        }

        void Wire(string source, string target, string input)
        {
            wires.Add(new GraphConnection(Reference(source), new PortReference(target, input)));
        }

        void Parameter(string id, ValueType type, decimal value)
        {
            Node(id, "Parameter", new Dictionary<string, ParamValue> { ["name"] = Text(id), ["value"] = Number(value) }, type);
        }

        void Constant(string id, ValueType type, decimal value)
        {
            Node(id, "Constant", new Dictionary<string, ParamValue> { ["value"] = Number(value) }, type);
        }

        void Unary(string id, string type, string source)
        {
            Node(id, type);
            Wire(source, id, "in");
        }

        void Binary(string id, string type, string operation, string a, string b)
        {
            Node(id, type, new Dictionary<string, ParamValue> { ["op"] = Text(operation) });
            Wire(a, id, "a");
            Wire(b, id, "b");
        }

        void And(string id, params string[] conditions)
        {
            string previous = conditions[0];

            for (var index = 1; index < conditions.Length; index++)
            {
                string step = index == conditions.Length - 1 ? id : id + index;
                Node(step, "And");
                Wire(previous, step, "a");
                Wire(conditions[index], step, "b");
                previous = step;
            }
        }

        void Flat(string id, string preview, string quantity)
        {
            Binary(id + "Zero", "Compare", "=", quantity, "zeroBase");
            string[] reasons = ["ZeroQuantity", "BelowMinQuantity", "BelowMinNotional"];

            foreach (string reason in reasons)
            {
                Node(
                    id + reason,
                    "Constant",
                    new Dictionary<string, ParamValue>
                    {
                        ["value"] = new FixedParamValue(new RejectionReasonParamLiteral(Enum.Parse<RejectionReason>(reason)))
                    },
                    ValueType.RejectionReason);
                Binary(id + "Is" + reason, "Compare", "=", preview + ".reason", id + reason);
            }

            Node(id + "DustFirst", "Or");
            Wire(id + "IsZeroQuantity", id + "DustFirst", "a");
            Wire(id + "IsBelowMinQuantity", id + "DustFirst", "b");
            Node(id + "Dust", "Or");
            Wire(id + "DustFirst", id + "Dust", "a");
            Wire(id + "IsBelowMinNotional", id + "Dust", "b");
            Unary(id + "Invalid", "Not", preview + ".valid");
            And(id + "InvalidDust", id + "Invalid", id + "Dust");
            Node(id, "Or");
            Wire(id + "Zero", id, "a");
            Wire(id + "InvalidDust", id, "b");
        }

        void Filter(string id, string source, string condition)
        {
            Node(id, "FilterEvent");
            Wire(source, id, "event");
            Wire(condition, id, "condition");
        }

        void Merge(string id, string a, string b)
        {
            Node(id, "MergeEvents");
            Wire(a, id, "a");
            Wire(b, id, "b");
        }

        void Preview(string id, string side, string type, string amount, string? price)
        {
            Node(id, "OrderSizePreview", new Dictionary<string, ParamValue> { ["side"] = Text(side), ["type"] = Text(type) });
            Wire(amount, id, side == "Buy" ? "amount" : "quantity");

            if (price is not null)
            {
                Wire(price, id, "price");
            }
        }

        void Action(string id, string type, string trigger, string enabled, string tag, string amountKey, string amount, string? price)
        {
            var parameters = new Dictionary<string, ParamValue> { ["tag"] = Text(tag) };

            if (amountKey == "amount")
            {
                parameters[amountKey] = new WiredParamValue(Reference(amount));
            }

            Node(id, type, parameters, mode: ActionEvaluationMode.OnEvent);
            Wire(trigger, id, "trigger");
            Wire(enabled, id, "enabled");

            if (amountKey != "amount")
            {
                Wire(amount, id, amountKey);
            }

            if (price is not null)
            {
                Wire(price, id, "price");
            }
        }

        void Cancel(string id, string trigger, string tag)
        {
            Node(id, "CancelOrder", new Dictionary<string, ParamValue> { ["tag"] = Text(tag) }, mode: ActionEvaluationMode.OnEvent);
            Wire(trigger, id, "trigger");
        }
    }

    private static PortReference Reference(string source)
    {
        string[] pieces = source.Split('.');

        return new PortReference(pieces[0], pieces.Length == 1 ? "out" : pieces[1]);
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