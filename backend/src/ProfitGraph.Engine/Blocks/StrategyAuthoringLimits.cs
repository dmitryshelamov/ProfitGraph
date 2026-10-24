using ProfitGraph.Engine.Graphs.Validation;

namespace ProfitGraph.Engine.Blocks;

public static class StrategyAuthoringLimits
{
    public const int Blocks = 128;
    public const int Values = 256;
    public const int Parameters = 128;
    public const int Wires = 512;
    public const int ChecksPerBlock = 32;
    public const int Checks = 256;
    public const int ExpressionNodes = 2048;
    public const int InlineDepth = 32;
    public const int ExpandedDepth = 256;
    public const int IdentifierLength = 128;
    public const int TextLength = 1024;
    public const int TotalTextLength = 1048576;
    public const int Diagnostics = 128;
    public const int DiagnosticWork = 2048;

    public static IReadOnlyList<StrategyBlockDiagnostic> Inspect(StrategyDefinition definition)
    {
        var errors = new List<StrategyBlockDiagnostic>();
        var pending = new Stack<(ValueExpression Expression, int Depth, StrategySource Source)>();
        var dependencies = new Dictionary<string, List<(string Name, int Depth)>>(StringComparer.Ordinal);
        var depths = new Dictionary<string, int>(StringComparer.Ordinal);
        var roots = new List<(string Name, int Depth, StrategySource Source)>();
        var count = 0;
        var textCount = 0;

        void Error(StrategySource source)
        {
            if (errors.Count < Diagnostics)
            {
                errors.Add(
                    new StrategyBlockDiagnostic(
                        GraphDiagnosticCode.InvalidParameter,
                        "Strategy authoring limit exceeded.",
                        source.BlockId,
                        source.ValueName,
                        source.ParameterName,
                        source.CheckIndex));
            }
            else if (errors.Count == Diagnostics)
            {
                errors.Add(new StrategyBlockDiagnostic(GraphDiagnosticCode.InvalidParameter, "Authoring limit diagnostics truncated."));
            }
        }

        void Text(string? value, StrategySource source, bool identifier = false)
        {
            textCount += value?.Length ?? 0;

            if (value?.Length > (identifier ? IdentifierLength : TextLength) || textCount > TotalTextLength)
            {
                Error(source);
            }
        }

        void Expression(ValueExpression? expression, StrategySource source)
        {
            if (expression is not null)
            {
                pending.Push((expression, 1, source));
            }
        }

        if (definition.Blocks.Count > Blocks || definition.Values.Count > Values || definition.Parameters.Count > Parameters || definition.Wires.Count > Wires ||
            definition.Blocks.Sum(block => (long)block.Checks.Count) > Checks)
        {
            Error(new StrategySource());

            return errors;
        }

        foreach (StrategyParameter parameter in definition.Parameters)
        {
            Text(parameter.Name, new StrategySource(ParameterName: parameter.Name), identifier: true);
        }

        foreach (StrategyValue value in definition.Values)
        {
            var source = new StrategySource(ValueName: value.Name);
            Text(value.Name, source, identifier: true);

            if (value.Name is not null)
            {
                dependencies.TryAdd(value.Name, new List<(string Name, int Depth)>());
                depths.TryAdd(value.Name, value: 0);
            }

            Expression(value.Expression, source);
        }

        foreach (StrategyBlock block in definition.Blocks)
        {
            var source = new StrategySource(BlockId: block.Id);
            Text(block.Id, source, identifier: true);
            Text(block.DisplayName, source);
            Text(block.Kind, source);
            Text(block.Source, source);
            Text(block.Join, source);
            Text(block.Side, source);
            Text(block.SizeMode, source);
            Text(block.OrderKind, source);
            Expression(block.Amount, source);
            Expression(block.Quantity, source);
            Expression(block.Price, source);
            Expression(block.Tag, source);

            if (block.Checks.Count > ChecksPerBlock)
            {
                Error(source);
            }

            for (var index = 0; index < block.Checks.Count; index++)
            {
                StrategyCheck check = block.Checks[index];
                StrategySource checkSource = source with { CheckIndex = index };
                Text(check.Type, checkSource);
                Text(check.Operation, checkSource);
                Text(check.Side, checkSource);
                Text(check.OrderKind, checkSource);
                Text(check.SizeMode, checkSource);
                Expression(check.Tag, checkSource);

                if (check.Operands.Count > ExpressionNodes || pending.Count + check.Operands.Count > ExpressionNodes)
                {
                    Error(checkSource);

                    return errors;
                }

                foreach ((string key, ValueExpression operand) in check.Operands)
                {
                    Text(key, checkSource, identifier: true);
                    Expression(operand, checkSource);
                }
            }
        }

        foreach (StrategyWire wire in definition.Wires)
        {
            var source = new StrategySource(BlockId: wire.To);
            Text(wire.To, source, identifier: true);
            Text(wire.From.Block, source, identifier: true);
            Text(wire.From.Exit, source, identifier: true);
        }

        while (pending.TryPop(out (ValueExpression Expression, int Depth, StrategySource Source) item))
        {
            if (++count > ExpressionNodes || item.Depth > InlineDepth)
            {
                Error(item.Source);

                break;
            }

            ValueExpression expression = item.Expression;
            Text(expression.Kind, item.Source);
            Text(expression.Text, item.Source);
            Text(expression.Name, item.Source, identifier: true);
            Text(expression.Field, item.Source);
            Text(expression.Operation, item.Source);

            if (item.Source.ValueName is not null)
            {
                depths[item.Source.ValueName] = Math.Max(depths[item.Source.ValueName], item.Depth);
            }

            if (expression.Kind == "value" && expression.Name is not null)
            {
                if (item.Source.ValueName is not null)
                {
                    dependencies[item.Source.ValueName].Add((expression.Name, item.Depth));
                }
                else
                {
                    roots.Add((expression.Name, item.Depth, item.Source));
                }
            }

            if (expression.Arguments.Count > ExpressionNodes || pending.Count + expression.Arguments.Count + count > ExpressionNodes)
            {
                Error(item.Source);

                break;
            }

            foreach ((string key, ValueExpression argument) in expression.Arguments)
            {
                Text(key, item.Source, identifier: true);
                pending.Push((argument, item.Depth + 1, item.Source));
            }

            if (errors.Count > Diagnostics || textCount > TotalTextLength)
            {
                break;
            }
        }

        var resolved = new HashSet<string>(StringComparer.Ordinal);

        for (var iteration = 0; iteration < Values; iteration++)
        {
            var changed = false;

            foreach ((string name, List<(string Name, int Depth)> links) in dependencies)
            {
                if (resolved.Contains(name) || links.Any(link => dependencies.ContainsKey(link.Name) && !resolved.Contains(link.Name)))
                {
                    continue;
                }

                foreach ((string target, int depth) in links)
                {
                    depths[name] = Math.Max(depths[name], Math.Min(ExpandedDepth + 1, depth + depths.GetValueOrDefault(target)));
                }

                resolved.Add(name);
                changed = true;
            }

            if (!changed)
            {
                break;
            }
        }

        foreach ((string name, int depth) in depths)
        {
            if (depth > ExpandedDepth)
            {
                Error(new StrategySource(ValueName: name));
            }
        }

        foreach ((string name, int depth, StrategySource source) in roots)
        {
            if (depth + depths.GetValueOrDefault(name) > ExpandedDepth)
            {
                Error(source);
            }
        }

        return errors.Count <= Diagnostics
            ? errors
            : errors.Take(Diagnostics)
                .Append(new StrategyBlockDiagnostic(GraphDiagnosticCode.InvalidParameter, "Authoring limit diagnostics truncated."))
                .ToArray();
    }
}