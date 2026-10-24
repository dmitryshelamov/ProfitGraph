using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Graphs.Validation;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Nodes;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Blocks;

public sealed class StrategyDefinitionValidator
{
    private static IReadOnlyList<StrategyBlockDiagnostic> BoundDiagnostics(IEnumerable<StrategyBlockDiagnostic> diagnostics)
    {
        StrategyBlockDiagnostic[] errors = diagnostics.Distinct().Take(StrategyAuthoringLimits.Diagnostics + 1).ToArray();

        return errors.Length <= StrategyAuthoringLimits.Diagnostics
            ? errors
            : errors.Take(StrategyAuthoringLimits.Diagnostics)
                .Append(new StrategyBlockDiagnostic(GraphDiagnosticCode.InvalidParameter, "Diagnostic limit exceeded; diagnostics truncated."))
                .ToArray();
    }

    public StrategyBlockValidationResult Validate(StrategyDefinition definition, IReadOnlyList<INodeDefinition> definitions)
    {
        var diagnostics = new List<StrategyBlockDiagnostic>();
        IReadOnlyList<StrategyBlockDiagnostic> limitErrors = StrategyAuthoringLimits.Inspect(definition);

        if (limitErrors.Count > 0)
        {
            return new StrategyBlockValidationResult(limitErrors);
        }

        Dictionary<string, StrategyValue> values = definition.Values.Where(value => !string.IsNullOrWhiteSpace(value.Name))
            .GroupBy(value => value.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var valueDiagnostics = new Dictionary<string, HashSet<StrategyBlockDiagnostic>>(StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> dependents = values.Keys.ToDictionary(name => name, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var visitingValues = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> parameters = definition.Parameters.Select(parameter => parameter.Name).ToHashSet(StringComparer.Ordinal);

        Duplicates(definition.Blocks.Select(block => block.Id), "Block", id => new StrategySource(BlockId: id));
        Duplicates(definition.Values.Select(value => value.Name), "Value", name => new StrategySource(ValueName: name));
        Duplicates(definition.Parameters.Select(parameter => parameter.Name), "Parameter", name => new StrategySource(ParameterName: name));

        if (definition.Blocks.Count == 0)
        {
            Add(GraphDiagnosticCode.EmptyGraph, "Add at least one block.", new StrategySource());
        }

        foreach (StrategyParameter parameter in definition.Parameters)
        {
            if (!UnitAlgebra.IsNumeric(parameter.ValueType) ||
                (parameter.ValueType == ValueType.Integer && decimal.Truncate(parameter.DefaultValue) != parameter.DefaultValue))
            {
                Add(GraphDiagnosticCode.InvalidParameter, "Parameter default must match its numeric value type.", new StrategySource(ParameterName: parameter.Name));
            }
        }

        foreach (StrategyValue value in definition.Values)
        {
            if (value.Name is not null && values.ContainsKey(value.Name))
            {
                _ = NamedValue(value.Name);
            }
        }

        // Complete diagnostic propagation across cycles as well as acyclic sharing, without revalidating any value body.
        var pending = new Queue<(string Value, StrategyBlockDiagnostic Diagnostic)>(
            valueDiagnostics.SelectMany(pair => pair.Value.Select(diagnostic => (pair.Key, diagnostic))));

        var diagnosticWork = 0;

        while (diagnosticWork++ < StrategyAuthoringLimits.DiagnosticWork && pending.TryDequeue(out (string Value, StrategyBlockDiagnostic Diagnostic) item))
        {
            foreach (string dependent in dependents[item.Value])
            {
                StrategyBlockDiagnostic mapped = AtSource(item.Diagnostic, new StrategySource(ValueName: dependent));

                if (valueDiagnostics[dependent].Add(mapped))
                {
                    pending.Enqueue((dependent, mapped));
                }
            }
        }

        if (pending.Count > 0)
        {
            diagnostics.Add(new StrategyBlockDiagnostic(GraphDiagnosticCode.InvalidParameter, "Diagnostic fanout limit exceeded; diagnostics truncated."));
        }

        foreach (HashSet<StrategyBlockDiagnostic> result in valueDiagnostics.Values)
        {
            diagnostics.AddRange(result);
        }

        foreach (StrategyBlock block in definition.Blocks)
        {
            var source = new StrategySource(BlockId: block.Id);

            switch (block.Kind)
            {
                case "event":
                    if (block.Source is not ("OnStart" or "OnResume" or "OnCandleClosed" or "OnOrderFilled" or "OnOrderCancelled" or "OnOrderRejected"))
                    {
                        Add(GraphDiagnosticCode.UnknownNodeType, "Unknown event source.", source);
                    }

                    break;
                case "condition":
                    if (block.Join is not ("all" or "any") || block.Checks.Count == 0)
                    {
                        Add(GraphDiagnosticCode.MissingInput, "Choose all/any and add at least one check.", source);
                    }

                    for (var checkIndex = 0; checkIndex < block.Checks.Count; checkIndex++)
                    {
                        StrategyCheck check = block.Checks[checkIndex];
                        source = new StrategySource(block.Id, CheckIndex: checkIndex);
                        string[] required = check.Type switch
                        {
                            "Compare" or "CrossesAbove" or "CrossesBelow" => ["a", "b"],
                            "PositionFlatPreview" => ["quantity"],
                            "OrderSizePreviewValid" => [check.SizeMode ?? (check.Side == "Buy" ? "amount" : "quantity")],
                            "PositionFlat" or "NoActiveOrders" or "NoActiveTaggedOrder" or "HasBaseBalance" => [],
                            _ => []
                        };

                        if (!StrategyBlockSchema.CheckTypes.Contains(check.Type))
                        {
                            Add(GraphDiagnosticCode.UnknownNodeType, "Unknown check type.", source);
                        }

                        if (check.Type == "Compare" && check.Operation is not ("=" or "!=" or ">" or ">=" or "<" or "<="))
                        {
                            Add(GraphDiagnosticCode.InvalidParameter, "Choose a comparison operation.", source);
                        }

                        if ((check.Type == "PositionFlatPreview" && check.Operation is not (null or "out" or "valid")) ||
                            (check.Type == "NoActiveTaggedOrder" && check.Operation is not (null or "out" or "exists")))
                        {
                            Add(GraphDiagnosticCode.InvalidParameter, "Choose a supported check output.", source);
                        }

                        if (check.Type == "OrderSizePreviewValid")
                        {
                            OrderSettings(check.OrderKind, check.Side, source);
                            MinimumPolicy(check.MinimumOrderPolicy, check.Side, source);

                            string checkSize = check.SizeMode ?? (check.Side == "Buy" ? "amount" : "quantity");

                            if (!StrategyBlockSchema.SizeModes.Contains(checkSize))
                            {
                                Add(GraphDiagnosticCode.InvalidParameter, "Choose an amount or quantity order size.", source);
                            }

                            bool inverse = checkSize != (check.Side == "Buy" ? "amount" : "quantity");

                            if (inverse && check.Operands.TryGetValue("price", out ValueExpression? conversionPrice))
                            {
                                PositivePrice(conversionPrice, source);
                            }

                            if (check.OrderKind == "Limit" || inverse)
                            {
                                required = required.Append("price").ToArray();
                            }
                        }

                        if (check.Type == "NoActiveTaggedOrder" && check.Tag is null)
                        {
                            Add(GraphDiagnosticCode.MissingParameter, "Specify the order tag.", source);
                        }

                        foreach (string operand in required)
                        {
                            if (!check.Operands.ContainsKey(operand))
                            {
                                Add(GraphDiagnosticCode.MissingInput, "Missing check operand: " + operand + ".", source);
                            }
                        }

                        foreach (ValueExpression operand in check.Operands.Values)
                        {
                            InspectExpression(operand, source);
                        }

                        Optional(check.Tag, source);
                    }

                    source = new StrategySource(BlockId: block.Id);

                    break;
                case "order":
                    OrderSettings(block.OrderKind, block.Side, source);
                    MinimumPolicy(block.MinimumOrderPolicy, block.Side, source);

                    string sizeMode = block.SizeMode ?? (block.Side == "Buy" ? "amount" : "quantity");

                    if (!StrategyBlockSchema.SizeModes.Contains(sizeMode))
                    {
                        Add(GraphDiagnosticCode.InvalidParameter, "Choose an amount or quantity order size.", source);
                    }

                    bool inverseOrder = sizeMode != (block.Side == "Buy" ? "amount" : "quantity");

                    if (inverseOrder && block.Price is not null)
                    {
                        PositivePrice(block.Price, source);
                    }

                    bool requiresPrice = block.OrderKind == "Limit" || (block.OrderKind == "Market" &&
                        ((block.Side == "Buy" && sizeMode == "quantity") || (block.Side == "Sell" && sizeMode == "amount")));

                    if ((sizeMode == "amount" && block.Amount is null) || (sizeMode == "quantity" && block.Quantity is null) ||
                        (requiresPrice && block.Price is null))
                    {
                        Add(GraphDiagnosticCode.MissingInput, "Specify the selected order size and its required price.", source);
                    }

                    break;
                case "cancel":
                    if (block.Tag is null)
                    {
                        Add(GraphDiagnosticCode.MissingParameter, "Specify the cancellation tag.", source);
                    }

                    break;
                default:
                    Add(GraphDiagnosticCode.UnknownNodeType, "Unknown block kind.", source);

                    break;
            }

            Optional(block.Amount, source);
            Optional(block.Quantity, source);
            Optional(block.Price, source);
            Optional(block.Tag, source);
        }

        foreach (StrategyWire wire in definition.Wires)
        {
            StrategyBlock? origin = definition.Blocks.FirstOrDefault(block => block.Id == wire.From.Block);
            StrategyBlock? target = definition.Blocks.FirstOrDefault(block => block.Id == wire.To);

            if (origin is null || target is null)
            {
                Add(GraphDiagnosticCode.UnknownNode, "A wire references a missing block.", new StrategySource(BlockId: origin?.Id ?? target?.Id));
            }
            else if (!Exits(origin).Contains(wire.From.Exit, StringComparer.Ordinal))
            {
                Add(GraphDiagnosticCode.UnknownPort, "Unknown block exit: " + wire.From.Exit + ".", new StrategySource(BlockId: origin.Id));
            }
            else if (target.Kind == "event")
            {
                Add(GraphDiagnosticCode.IncompatiblePort, "An event source has no event input.", new StrategySource(BlockId: target.Id));
            }
        }

        if (diagnostics.Count > 0)
        {
            return new StrategyBlockValidationResult(BoundDiagnostics(diagnostics));
        }

        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(definitions).Lower(definition);

        foreach (GraphDiagnostic diagnostic in new GraphValidator().ValidateStrict(lowered.Graph, definitions).Diagnostics)
        {
            diagnostics.Add(StrategyBlockDiagnosticMapper.Map(diagnostic, lowered.Sources));
        }

        return new StrategyBlockValidationResult(BoundDiagnostics(diagnostics));

        void Duplicates(IEnumerable<string> identities, string label, Func<string, StrategySource> map)
        {
            foreach (IGrouping<string, string> group in identities.GroupBy(id => id, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(group.Key) || group.Key.StartsWith('$') ||
                    (label == "Block" && group.Key.StartsWith("composite:", StringComparison.Ordinal)) || group.Count() != 1)
                {
                    Add(GraphDiagnosticCode.DuplicateNodeId, label + " names/IDs must be non-empty, unique, and not start with '$'.", map(group.Key));
                }
            }
        }

        void Add(GraphDiagnosticCode code, string message, StrategySource source)
        {
            diagnostics.Add(new StrategyBlockDiagnostic(code, message, source.BlockId, source.ValueName, source.ParameterName, source.CheckIndex));
        }

        void PositivePrice(ValueExpression expression, StrategySource source)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var knownValues = new Dictionary<string, decimal?>(StringComparer.Ordinal);
            decimal? price = Known(expression);

            if (price <= 0m)
            {
                Add(GraphDiagnosticCode.InvalidParameter, "Conversion price must be positive.", source);
            }

            decimal? Known(ValueExpression value)
            {
                if (value.Kind == "constant")
                {
                    return value.Number;
                }

                if (value.Kind == "parameter")
                {
                    return definition.Parameters.FirstOrDefault(parameter => parameter.Name == value.Name)?.DefaultValue;
                }

                if (value.Kind == "value" && value.Name is not null && visited.Add(value.Name) && values.TryGetValue(value.Name, out StrategyValue? named))
                {
                    decimal? result = Known(named.Expression);
                    knownValues[value.Name] = result;

                    return result;
                }

                if (value.Kind == "value" && value.Name is not null && knownValues.TryGetValue(value.Name, out decimal? cached))
                {
                    return cached;
                }

                if (value.Kind == "arithmetic" && value.Arguments.TryGetValue("a", out ValueExpression? a) && value.Arguments.TryGetValue("b", out ValueExpression? b))
                {
                    decimal? left = Known(a);
                    decimal? right = Known(b);

                    if (left.HasValue && right.HasValue)
                    {
                        try
                        {
                            return value.Operation switch
                            {
                                "Add" => left.Value + right.Value, "Subtract" => left.Value - right.Value,
                                "Multiply" => left.Value * right.Value, "Divide" when right.Value != 0m => left.Value / right.Value,
                                "Min" => Math.Min(left.Value, right.Value), "Max" => Math.Max(left.Value, right.Value),
                                "Modulo" when right.Value != 0m => left.Value % right.Value, _ => null
                            };
                        }
                        catch (OverflowException)
                        {
                            return null;
                        }
                    }
                }

                return null;
            }
        }

        void MinimumPolicy(string? policy, string? side, StrategySource source)
        {
            if (policy is not (null or "Reject" or "RaiseToExchangeMinimum") || (policy == "RaiseToExchangeMinimum" && side != "Buy"))
            {
                Add(GraphDiagnosticCode.InvalidParameter, "Choose a supported minimum order policy for a buy order.", source);
            }
        }

        void OrderSettings(string? kind, string? side, StrategySource source)
        {
            if (!StrategyBlockSchema.OrderKinds.Contains(kind!) || !StrategyBlockSchema.Sides.Contains(side!))
            {
                Add(GraphDiagnosticCode.InvalidParameter, "Choose Market/Limit and Buy/Sell.", source);
            }
        }

        void Optional(ValueExpression? expression, StrategySource source)
        {
            if (expression is not null)
            {
                InspectExpression(expression, source);
            }
        }

        IReadOnlyCollection<StrategyBlockDiagnostic> NamedValue(string name)
        {
            if (valueDiagnostics.TryGetValue(name, out HashSet<StrategyBlockDiagnostic>? cached))
            {
                return cached;
            }

            if (!visitingValues.Add(name))
            {
                return [new StrategyBlockDiagnostic(GraphDiagnosticCode.Cycle, "Named values contain a cycle.", ValueName: name)];
            }

            var output = new HashSet<StrategyBlockDiagnostic>();
            Expression(values[name].Expression, new StrategySource(ValueName: name), output);
            visitingValues.Remove(name);
            valueDiagnostics.Add(name, output);

            return output;
        }

        StrategyBlockDiagnostic AtSource(StrategyBlockDiagnostic diagnostic, StrategySource source) => diagnostic.ParameterName is not null
            ? diagnostic
            : diagnostic with { BlockId = source.BlockId, ValueName = source.ValueName, CheckIndex = source.CheckIndex };

        void InspectExpression(ValueExpression expression, StrategySource source)
        {
            var output = new HashSet<StrategyBlockDiagnostic>();
            Expression(expression, source, output);
            diagnostics.AddRange(output);
        }

        void Expression(ValueExpression expression, StrategySource source, HashSet<StrategyBlockDiagnostic> output)
        {
            string[] allowedArguments = expression.Kind switch
            {
                "SMA" => ["in", "period"], "ATR" => ["period"], "previous" => ["in"], "arithmetic" => ["a", "b"], "activeOrder" => ["tag"], _ => []
            };

            foreach (string argument in expression.Arguments.Keys)
            {
                if (!allowedArguments.Contains(argument, StringComparer.Ordinal))
                {
                    AddExpression(GraphDiagnosticCode.UnknownParameter, "Unknown value operand: " + argument + ".", source);
                }
            }

            if (expression.Kind == "value")
            {
                if (expression.Name is null || !values.ContainsKey(expression.Name))
                {
                    AddExpression(GraphDiagnosticCode.UnknownNode, "Unknown named value.", source);
                }
                else
                {
                    if (source.ValueName is { } owner)
                    {
                        dependents[expression.Name].Add(owner);
                    }

                    foreach (StrategyBlockDiagnostic diagnostic in NamedValue(expression.Name))
                    {
                        output.Add(AtSource(diagnostic, source));
                    }
                }
            }
            else if (expression.Kind == "parameter")
            {
                if (expression.Name is null || !parameters.Contains(expression.Name))
                {
                    AddExpression(GraphDiagnosticCode.UnknownParameter, "Unknown parameter reference.", source);
                }
            }
            else
            {
                string[] required = expression.Kind switch
                {
                    "SMA" or "previous" => ["in"], "arithmetic" => ["a", "b"], _ => []
                };

                if (expression.Kind is "SMA" or "ATR")
                {
                    required = required.Append("period").ToArray();
                }

                if (expression.Kind is not ("constant" or "candle" or "SMA" or "ATR" or "previous" or "balance" or "position" or "activeOrder" or "arithmetic"))
                {
                    AddExpression(GraphDiagnosticCode.UnknownNodeType, "Unknown value expression kind.", source);
                }

                if (expression.Kind == "constant" && (!expression.ValueType.HasValue || expression.Number.HasValue == expression.Text is not null))
                {
                    AddExpression(GraphDiagnosticCode.InvalidParameter, "A constant needs a value type and exactly one literal.", source);
                }

                if (expression.Kind == "constant" && expression.ValueType.HasValue)
                {
                    ValueType type = expression.ValueType.Value;
                    bool validLiteral = UnitAlgebra.IsNumeric(type)
                        ? expression.Number.HasValue && (type != ValueType.Integer || decimal.Truncate(expression.Number.Value) == expression.Number.Value)
                        : type switch
                        {
                            ValueType.Tag => !string.IsNullOrWhiteSpace(expression.Text),
                            ValueType.Boolean => bool.TryParse(expression.Text, out _),
                            ValueType.RejectionReason => Enum.TryParse(expression.Text, out RejectionReason reason) && Enum.IsDefined(reason),
                            ValueType.OrderId => expression.Number is > 0m and <= long.MaxValue && decimal.Truncate(expression.Number.Value) == expression.Number.Value,
                            _ => false
                        };

                    if (!validLiteral)
                    {
                        AddExpression(GraphDiagnosticCode.InvalidParameter, "Constant literal does not match a supported value type.", source);
                    }
                }

                if (expression.Kind is "candle" or "balance" or "position" or "activeOrder" && string.IsNullOrWhiteSpace(expression.Field))
                {
                    AddExpression(GraphDiagnosticCode.MissingParameter, "Choose a value field.", source);
                }

                if (!string.IsNullOrWhiteSpace(expression.Field))
                {
                    string? typeKey = expression.Kind switch
                    {
                        "candle" => "CandleField", "balance" => "Balance", "position" => "Position", "activeOrder" => "ActiveOrder", _ => null
                    };
                    INodeDefinition? fieldDefinition = definitions.FirstOrDefault(candidate => candidate.TypeKey == typeKey);
                    bool validField = expression.Kind == "candle"
                        ? fieldDefinition?.Params.FirstOrDefault(parameter => parameter.Key == "field")
                            ?.AllowedValues?.Contains(expression.Field, StringComparer.Ordinal) ?? true
                        : fieldDefinition is null || fieldDefinition.Outputs.Any(output => output.Key == expression.Field);

                    if (!validField)
                    {
                        AddExpression(GraphDiagnosticCode.UnknownPort, "Unknown value field: " + expression.Field + ".", source);
                    }
                }

                if (expression.Kind == "activeOrder" && expression.Text is null == !expression.Arguments.ContainsKey("tag"))
                {
                    AddExpression(GraphDiagnosticCode.MissingParameter, "Specify exactly one active-order tag source.", source);
                }

                if (expression.Kind == "arithmetic" && expression.Operation is not ("Add" or "Subtract" or "Multiply" or "Divide" or "Min" or "Max" or "Modulo"))
                {
                    AddExpression(GraphDiagnosticCode.InvalidParameter, "Choose an arithmetic operation.", source);
                }

                foreach (string operand in required)
                {
                    if (!expression.Arguments.ContainsKey(operand))
                    {
                        AddExpression(GraphDiagnosticCode.MissingInput, "Missing value operand: " + operand + ".", source);
                    }
                }

                if (expression.Arguments.TryGetValue("period", out ValueExpression? period) && period.Kind == "parameter" && period.Name is not null)
                {
                    StrategyParameter? parameter = definition.Parameters.FirstOrDefault(candidate => candidate.Name == period.Name);

                    if (parameter is not null && (parameter.ValueType != ValueType.Integer || parameter.DefaultValue < 1m || parameter.DefaultValue > 200m))
                    {
                        AddExpression(
                            GraphDiagnosticCode.InvalidParameter,
                            "Indicator periods must be integers from 1 through 200.",
                            new StrategySource(ParameterName: parameter.Name));
                    }
                }
            }

            foreach (ValueExpression argument in expression.Arguments.Values)
            {
                Expression(argument, source, output);
            }

            void AddExpression(GraphDiagnosticCode code, string message, StrategySource owner)
            {
                output.Add(new StrategyBlockDiagnostic(code, message, owner.BlockId, owner.ValueName, owner.ParameterName, owner.CheckIndex));
            }
        }
    }

    public static IReadOnlyList<string> Exits(StrategyBlock block)
    {
        return block.Kind switch
        {
            "event" => ["out"], "condition" => ["yes", "no"], "order" => ["submitted", "filled", "cancelled", "rejected"],
            "cancel" => ["completed", "rejected"], _ => []
        };
    }
}