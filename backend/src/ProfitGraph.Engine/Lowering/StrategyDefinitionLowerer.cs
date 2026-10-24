using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Composites;
using ProfitGraph.Engine.Nodes;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Lowering;

public sealed class StrategyDefinitionLowerer(IReadOnlyList<INodeDefinition> definitions)
{
    public static string ParameterNodeId(string name)
    {
        return GeneratedId("parameter:" + Encode(name));
    }

    public StrategyLoweringResult Lower(StrategyDefinition definition, IReadOnlyDictionary<string, DecimalOptimizeParamValue>? ranges = null)
    {
        if (StrategyAuthoringLimits.Inspect(definition).Count > 0)
        {
            throw new ArgumentException("Strategy authoring limit exceeded.");
        }

        return new Lowering(definition, ranges, definitions).Run();
    }

    private static string Encode(string segment)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(segment));
    }

    private static string GeneratedId(string identity)
    {
        return "$" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private sealed class Lowering
    {
        private readonly StrategyDefinition _definition;
        private readonly IReadOnlyDictionary<string, INodeDefinition> _definitions;
        private readonly IReadOnlyDictionary<string, DecimalOptimizeParamValue> _ranges;
        private readonly List<GraphNode> _nodes = new List<GraphNode>();
        private readonly List<GraphConnection> _connections = new List<GraphConnection>();
        private readonly Dictionary<string, StrategySource> _sources = new Dictionary<string, StrategySource>(StringComparer.Ordinal);
        private readonly Dictionary<string, PortReference> _values = new Dictionary<string, PortReference>(StringComparer.Ordinal);
        private readonly Dictionary<string, PortReference> _eventTargets = new Dictionary<string, PortReference>(StringComparer.Ordinal);
        private readonly HashSet<string> _visiting = new HashSet<string>(StringComparer.Ordinal);

        public Lowering(StrategyDefinition definition, IReadOnlyDictionary<string, DecimalOptimizeParamValue>? ranges, IReadOnlyList<INodeDefinition> definitions)
        {
            _definition = definition;
            _definitions = definitions.ToDictionary(item => item.TypeKey, StringComparer.Ordinal);
            _ranges = ranges ?? new Dictionary<string, DecimalOptimizeParamValue>();
        }

        public StrategyLoweringResult Run()
        {
            foreach (StrategyParameter parameter in _definition.Parameters)
            {
                Node(
                    ParameterNodeId(parameter.Name),
                    "Parameter",
                    new StrategySource(ParameterName: parameter.Name),
                    new Dictionary<string, ParamValue>
                    {
                        ["name"] = Text(parameter.Name),
                        ["value"] = _ranges.TryGetValue(parameter.Name, out DecimalOptimizeParamValue? range) ? range : Number(parameter.DefaultValue)
                    },
                    parameter.ValueType,
                    identities: new Dictionary<string, PortReference> { ["value"] = new PortReference(parameter.Name, "value") });
            }

            foreach (StrategyValue value in _definition.Values)
            {
                Named(value.Name);
            }

            foreach (StrategyBlock block in _definition.Blocks)
            {
                Block(block);
            }

            foreach (IGrouping<string, StrategyWire> group in _definition.Wires.GroupBy(wire => wire.To, StringComparer.Ordinal))
            {
                StrategyBlock block = _definition.Blocks.Single(candidate => candidate.Id == group.Key);
                PortReference[] incoming = group.Select(wire => new PortReference(wire.From.Block, wire.From.Exit)).ToArray();
                PortReference source = incoming[0];

                for (var index = 1; index < incoming.Length; index++)
                {
                    string merge = GeneratedId("merge:" + Encode(block.Id) + ":" + index);
                    Node(merge, "MergeEvents", new StrategySource(BlockId: block.Id));
                    Wire(source, merge, "a");
                    Wire(incoming[index], merge, "b");
                    source = new PortReference(merge, "out");
                }

                PortReference target = _eventTargets.GetValueOrDefault(block.Id) ?? new PortReference(block.Id, block.Kind == "condition" ? "event" : "trigger");
                Wire(source, target.NodeId, target.PortKey);
            }

            return new StrategyLoweringResult(
                new StrategyGraph(_nodes, _connections, _definition.TemplateKind),
                new ReadOnlyDictionary<string, StrategySource>(_sources));
        }

        private PortReference Named(string name)
        {
            if (_values.TryGetValue(name, out PortReference? found))
            {
                return found;
            }

            if (!_visiting.Add(name))
            {
                throw new ArgumentException("Named values contain a cycle.");
            }

            StrategyValue value = _definition.Values.Single(candidate => candidate.Name == name);
            PortReference result = Expression(value.Expression, "value:" + Encode(name), new StrategySource(ValueName: name));
            _values.Add(name, result);
            _visiting.Remove(name);

            return result;
        }

        private PortReference Expression(ValueExpression expression, string identity, StrategySource source)
        {
            if (expression.Kind == "parameter")
            {
                return new PortReference(ParameterNodeId(expression.Name!), "out");
            }

            if (expression.Kind == "value")
            {
                return Named(expression.Name!);
            }

            string id = GeneratedId(identity);
            string type = expression.Kind switch
            {
                "constant" => "Constant", "candle" => "CandleField", "previous" => "PreviousCandleValue", "arithmetic" => "Arithmetic",
                "balance" => "Balance", "position" => "Position", "activeOrder" => "ActiveOrder", "SMA" => "SMA", "ATR" => "ATR",
                _ => throw new ArgumentException("Unknown value expression kind: " + expression.Kind)
            };
            var parameters = new Dictionary<string, ParamValue>();

            if (expression.Kind == "constant")
            {
                parameters["value"] = Literal(expression);
            }

            if (expression.Kind == "candle")
            {
                parameters["field"] = Text(expression.Field!);
            }

            if (expression.Kind == "activeOrder" && expression.Text is not null)
            {
                parameters["tag"] = Text(expression.Text!);
            }

            if (expression.Kind == "arithmetic")
            {
                parameters["op"] = Text(expression.Operation!);
            }

            Node(id, type, source, parameters, expression.Kind == "constant" ? expression.ValueType : null);

            foreach ((string key, ValueExpression argument) in expression.Arguments.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                PortReference operand = Expression(argument, identity + ":" + Encode(key), source);

                if (key is "period" or "tag")
                {
                    ReplaceParameter(id, key, new WiredParamValue(operand));
                }
                else
                {
                    Wire(operand, id, key);
                }
            }

            return new PortReference(id, expression.Kind is "balance" or "position" or "activeOrder" ? expression.Field! : "out");
        }

        private void Block(StrategyBlock block)
        {
            var source = new StrategySource(BlockId: block.Id);
            var parameters = new Dictionary<string, ParamValue>();

            if (block.Kind == "event")
            {
                if (block.Tag is not null)
                {
                    parameters["tag"] = Binding(block.Tag, "block:" + Encode(block.Id) + ":tag", source);
                }

                Node(block.Id, block.Source!, source, parameters, name: block.DisplayName);

                return;
            }

            if (block.Kind == "condition")
            {
                Node(block.Id, "Decision", source, name: block.DisplayName);
                PortReference condition = Check(block.Checks[0], "block:" + Encode(block.Id) + ":check:0", source with { CheckIndex = 0 });

                for (var index = 1; index < block.Checks.Count; index++)
                {
                    PortReference next = Check(block.Checks[index], "block:" + Encode(block.Id) + ":check:" + index, source with { CheckIndex = index });
                    string join = GeneratedId("block:" + Encode(block.Id) + ":join:" + index);
                    Node(join, block.Join == "any" ? "Or" : "And", source);
                    Wire(condition, join, "a");
                    Wire(next, join, "b");
                    condition = new PortReference(join, "out");
                }

                Wire(condition, block.Id, "condition");

                return;
            }

            if (block.Tag is not null)
            {
                parameters["tag"] = Binding(block.Tag, "block:" + Encode(block.Id) + ":tag", source);
            }

            if (block.MinimumOrderPolicy == "RaiseToExchangeMinimum")
            {
                parameters["minimumOrderPolicy"] = Text(block.MinimumOrderPolicy);
            }

            string type = block.Kind == "cancel" ? "CancelOrder" : block.OrderKind + block.Side;
            Node(block.Id, type, source, parameters, mode: ActionEvaluationMode.OnEvent, name: block.DisplayName);

            if (block.Kind == "order")
            {
                OrderInputs(block, source);

                return;
            }

            foreach ((string key, ValueExpression? expression) in new[] { ("amount", block.Amount), ("quantity", block.Quantity), ("price", block.Price) })
            {
                if (expression is not null)
                {
                    // Use input connections so all operands retain their declared types.
                    Wire(Expression(expression, "block:" + Encode(block.Id) + ":" + Encode(key), source), block.Id, key);
                }
            }
        }

        private void OrderInputs(StrategyBlock block, StrategySource source)
        {
            string identity = "block:" + Encode(block.Id);
            string sizeMode = block.SizeMode ?? (block.Side == "Buy" ? "amount" : "quantity");
            string inputKey = block.Side == "Buy" ? "amount" : "quantity";
            ValueExpression? size = sizeMode == "amount" ? block.Amount : block.Quantity;
            bool inverse = sizeMode != inputKey;
            PortReference? price = block.Price is not null && inverse
                ? Expression(block.Price, identity + ":" + Encode("price"), source)
                : null;

            if (size is not null)
            {
                PortReference value = Expression(size, identity + ":" + Encode(inputKey), source);

                if (inverse && price is not null)
                {
                    (PortReference safePrice, PortReference positive) = ConversionPrice(price, identity + ":conversion", source);
                    value = Arithmetic(block.Side == "Buy" ? "Multiply" : "Divide", value, safePrice, identity + ":size", source);
                    string filter = GeneratedId(identity + ":positivePriceEvent");
                    Node(filter, "FilterEvent", source);
                    Wire(positive, filter, "condition");
                    Wire(new PortReference(filter, "out"), block.Id, "trigger");
                    _eventTargets[block.Id] = new PortReference(filter, "event");
                }

                Wire(value, block.Id, inputKey);
            }

            if (block.OrderKind == "Limit" && block.Price is not null)
            {
                price ??= Expression(block.Price, identity + ":" + Encode("price"), source);
                Wire(price, block.Id, "price");
            }
        }

        private PortReference Constant(decimal value, ValueType type, string identity, StrategySource source)
        {
            string id = GeneratedId(identity);
            Node(id, "Constant", source, new Dictionary<string, ParamValue> { ["value"] = Number(value) }, type);

            return new PortReference(id, "out");
        }

        private PortReference Arithmetic(string operation, PortReference a, PortReference b, string identity, StrategySource source)
        {
            string id = GeneratedId(identity);
            Node(id, "Arithmetic", source, new Dictionary<string, ParamValue> { ["op"] = Text(operation) });
            Wire(a, id, "a");
            Wire(b, id, "b");

            return new PortReference(id, "out");
        }

        private (PortReference Safe, PortReference Positive) ConversionPrice(PortReference price, string identity, StrategySource source)
        {
            PortReference zero = Constant(value: 0m, ValueType.Price, identity + ":zero", source);
            PortReference epsilon = Constant(value: 0.0000000000000000000000000001m, ValueType.Price, identity + ":epsilon", source);
            PortReference onePrice = Constant(value: 1m, ValueType.Price, identity + ":onePrice", source);
            PortReference oneScalar = Constant(value: 1m, ValueType.Scalar, identity + ":oneScalar", source);
            PortReference nonnegative = Arithmetic("Max", price, zero, identity + ":nonnegative", source);
            PortReference denominator = Arithmetic("Max", price, epsilon, identity + ":denominator", source);
            PortReference ratio = Arithmetic("Divide", nonnegative, denominator, identity + ":ratio", source);
            PortReference remainder = Arithmetic("Subtract", oneScalar, ratio, identity + ":remainder", source);
            PortReference fallback = Arithmetic("Multiply", onePrice, remainder, identity + ":fallback", source);
            PortReference safe = Arithmetic("Add", nonnegative, fallback, identity + ":safe", source);
            string compare = GeneratedId(identity + ":positive");
            Node(compare, "Compare", source, new Dictionary<string, ParamValue> { ["op"] = Text(">") });
            Wire(price, compare, "a");
            Wire(zero, compare, "b");

            return (safe, new PortReference(compare, "out"));
        }

        private PortReference Check(StrategyCheck check, string identity, StrategySource source)
        {
            string id = GeneratedId(identity);
            string type = check.Type == "OrderSizePreviewValid" ? "OrderSizePreview" : check.Type;
            var parameters = new Dictionary<string, ParamValue>();

            if (check.Operation is not null && check.Type == "Compare")
            {
                parameters["op"] = Text(check.Operation);
            }

            if (check.Tag is not null)
            {
                parameters["tag"] = Binding(check.Tag, identity + ":tag", source);
            }

            if (check.Side is not null)
            {
                parameters["side"] = Text(check.Side);
            }

            if (check.OrderKind is not null)
            {
                parameters["type"] = Text(check.OrderKind);
            }

            if (check.MinimumOrderPolicy == "RaiseToExchangeMinimum")
            {
                parameters["minimumOrderPolicy"] = Text(check.MinimumOrderPolicy);
            }

            Node(id, type, source, parameters);

            if (check.Type == "OrderSizePreviewValid")
            {
                string input = check.Side == "Buy" ? "amount" : "quantity";
                string selected = check.SizeMode ?? input;
                bool inverse = selected != input;
                PortReference? price = check.Operands.TryGetValue("price", out ValueExpression? priceExpression) && (inverse || check.OrderKind == "Limit")
                    ? Expression(priceExpression, identity + ":" + Encode("price"), source)
                    : null;
                PortReference? positive = null;

                if (check.Operands.TryGetValue(selected, out ValueExpression? sizeExpression))
                {
                    PortReference size = Expression(sizeExpression, identity + ":" + Encode(input), source);

                    if (inverse && price is not null)
                    {
                        (PortReference safePrice, PortReference validPrice) = ConversionPrice(price, identity + ":conversion", source);
                        positive = validPrice;
                        size = Arithmetic(check.Side == "Buy" ? "Multiply" : "Divide", size, safePrice, identity + ":size", source);
                    }

                    Wire(size, id, input);
                }

                if (check.OrderKind == "Limit" && price is not null)
                {
                    Wire(price, id, "price");
                }

                if (check.Operands.TryGetValue("quoteLimit", out ValueExpression? quoteLimit))
                {
                    Wire(Expression(quoteLimit, identity + ":quoteLimit", source), id, "quoteLimit");
                }

                if (positive is not null)
                {
                    string join = GeneratedId(identity + ":validPrice");
                    Node(join, "And", source);
                    Wire(new PortReference(id, "valid"), join, "a");
                    Wire(positive, join, "b");

                    return new PortReference(join, "out");
                }

                return new PortReference(id, "valid");
            }

            foreach ((string key, ValueExpression operand) in check.Operands.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (check.Type == "OrderSizePreviewValid" && key != (check.Side == "Buy" ? "amount" : "quantity") &&
                    !(key == "price" && check.OrderKind == "Limit"))
                {
                    continue;
                }

                if (check.Type == "HasBaseBalance" && key == "minQuantity")
                {
                    ReplaceParameter(id, key, Binding(operand, identity + ":" + Encode(key), source));
                }
                else
                {
                    Wire(Expression(operand, identity + ":" + Encode(key), source), id, key);
                }
            }

            return new PortReference(
                id,
                check.Type == "OrderSizePreviewValid" ? "valid" :
                check.Type == "NoActiveTaggedOrder" && check.Operation == "exists" ? "exists" :
                check.Type == "PositionFlatPreview" && check.Operation == "valid" ? "valid" : "out");
        }

        private ParamValue Binding(ValueExpression expression, string identity, StrategySource source)
        {
            return expression.Kind == "constant"
                ? Literal(expression)
                : new WiredParamValue(Expression(expression, identity, source));
        }

        private void Node(
            string id,
            string type,
            StrategySource source,
            IReadOnlyDictionary<string, ParamValue>? parameters = null,
            ValueType? declared = null,
            ActionEvaluationMode? mode = null,
            string? name = null,
            IReadOnlyDictionary<string, PortReference>? identities = null)
        {
            int schema = BuiltInComposites.Definitions.FirstOrDefault(definition => definition.TypeKey == type)?.SchemaVersion
                ?? (_definitions.TryGetValue(type, out INodeDefinition? registered) ? registered.SchemaVersion : 0);
            _nodes.Add(new GraphNode(id, type, schema, parameters ?? new Dictionary<string, ParamValue>(), declared, mode, identities, name));
            _sources.Add(id, source);
        }

        private void ReplaceParameter(string id, string key, ParamValue value)
        {
            int index = _nodes.FindIndex(node => node.Id == id);
            GraphNode node = _nodes[index];
            var parameters = new Dictionary<string, ParamValue>(node.Parameters) { [key] = value };
            _nodes[index] = new GraphNode(
                node.Id,
                node.TypeKey,
                node.SchemaVersion,
                parameters,
                node.DeclaredValueType,
                node.EvaluationMode,
                displayName: node.DisplayName);
        }

        private void Wire(PortReference source, string target, string key)
        {
            _connections.Add(new GraphConnection(source, new PortReference(target, key)));
        }

        private static FixedParamValue Literal(ValueExpression expression)
        {
            return expression.ValueType switch
            {
                ValueType.Boolean => new FixedParamValue(new BooleanParamLiteral(bool.Parse(expression.Text!))),
                ValueType.RejectionReason => new FixedParamValue(new RejectionReasonParamLiteral(Enum.Parse<RejectionReason>(expression.Text!))),
                ValueType.OrderId => new FixedParamValue(new OrderIdParamLiteral(checked((long)expression.Number!.Value))),
                _ => expression.Number.HasValue ? Number(expression.Number.Value) : Text(expression.Text!)
            };
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
}