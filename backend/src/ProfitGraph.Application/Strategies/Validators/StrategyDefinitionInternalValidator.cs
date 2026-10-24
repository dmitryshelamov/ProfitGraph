using FluentValidation;

using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Engine.Blocks;

namespace ProfitGraph.Application.Strategies.Validators;

/// <summary>
///     Checks snapshot shape while allowing incomplete editor drafts to be saved.
/// </summary>
internal sealed class StrategyDefinitionInternalValidator : AbstractValidator<StrategyDefinitionInternal>
{
    public StrategyDefinitionInternalValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleFor(definition => definition).Must(WithinTotalLimits).WithMessage("Strategy authoring limit exceeded.");
        RuleFor(definition => definition.TemplateKind).Must(kind => !kind.HasValue || Enum.IsDefined(kind.Value));
        RuleFor(definition => definition.Parameters).NotNull().Must(items => items is null || items.Count <= StrategyAuthoringLimits.Parameters);
        RuleFor(definition => definition.Values).NotNull().Must(items => items is null || items.Count <= StrategyAuthoringLimits.Values);
        RuleFor(definition => definition.Blocks).NotNull().Must(items => items is null || items.Count <= StrategyAuthoringLimits.Blocks);
        RuleFor(definition => definition.Wires).NotNull().Must(items => items is null || items.Count <= StrategyAuthoringLimits.Wires);
        RuleForEach(definition => definition.Parameters)
            .NotNull()
            .ChildRules(parameter =>
            {
                parameter.RuleFor(value => value.Name).NotEmpty().MaximumLength(128);
                parameter.RuleFor(value => value.ValueType).Must(Enum.IsDefined);
                parameter.RuleFor(value => value.DisplayUnit).Must(unit => !unit.HasValue || Enum.IsDefined(unit.Value));
            });
        RuleForEach(definition => definition.Values)
            .NotNull()
            .ChildRules(value =>
            {
                value.RuleFor(item => item.Name).NotEmpty().MaximumLength(128);
                value.RuleFor(item => item.Expression).NotNull().Must(IsWellFormed);
            });
        RuleForEach(definition => definition.Blocks)
            .NotNull()
            .ChildRules(block =>
            {
                block.RuleFor(value => value.Id).NotEmpty().MaximumLength(128);
                block.RuleFor(value => value.Kind).NotEmpty().MaximumLength(128);
                block.RuleFor(value => value.Tag).Must(value => value is null || IsWellFormed(value));
                block.RuleFor(value => value.Amount).Must(value => value is null || IsWellFormed(value));
                block.RuleFor(value => value.Quantity).Must(value => value is null || IsWellFormed(value));
                block.RuleFor(value => value.Price).Must(value => value is null || IsWellFormed(value));
                block.RuleFor(value => value.Checks)
                    .Must(checks => checks is null || (checks.Count <= StrategyAuthoringLimits.ChecksPerBlock && checks.All(check => check is not null &&
                        !string.IsNullOrWhiteSpace(check.Type) && (check.Tag is null || IsWellFormed(check.Tag)) &&
                        (check.Operands is null || check.Operands.All(pair => !string.IsNullOrWhiteSpace(pair.Key) && IsWellFormed(pair.Value))))));
            });
        RuleForEach(definition => definition.Wires)
            .NotNull()
            .ChildRules(wire =>
            {
                wire.RuleFor(value => value.From)
                    .NotNull()
                    .Must(from => from is not null && !string.IsNullOrWhiteSpace(from.Block) && !string.IsNullOrWhiteSpace(from.Exit));
                wire.RuleFor(value => value.To).NotEmpty();
            });
    }

    private static bool WithinTotalLimits(StrategyDefinitionInternal definition)
    {
        if ((definition.Blocks?.Count ?? 0) > StrategyAuthoringLimits.Blocks || (definition.Values?.Count ?? 0) > StrategyAuthoringLimits.Values ||
            (definition.Parameters?.Count ?? 0) > StrategyAuthoringLimits.Parameters || (definition.Wires?.Count ?? 0) > StrategyAuthoringLimits.Wires)
        {
            return false;
        }

        var expressions = new Stack<ValueExpressionInternal>();
        var textLength = 0;
        var checkCount = 0;

        bool Text(string? value, bool identifier = false)
        {
            textLength += value?.Length ?? 0;

            return (value?.Length ?? 0) <= (identifier ? StrategyAuthoringLimits.IdentifierLength : StrategyAuthoringLimits.TextLength) &&
                textLength <= StrategyAuthoringLimits.TotalTextLength;
        }

        void Push(ValueExpressionInternal? expression)
        {
            if (expression is not null)
            {
                expressions.Push(expression);
            }
        }

        foreach (StrategyValueInternal value in definition.Values ?? [])
        {
            if (value is null || !Text(value.Name, identifier: true))
            {
                return false;
            }

            Push(value.Expression);
        }

        foreach (StrategyParameterInternal parameter in definition.Parameters ?? [])
        {
            if (parameter is null || !Text(parameter.Name, identifier: true))
            {
                return false;
            }
        }

        foreach (StrategyBlockInternal block in definition.Blocks ?? [])
        {
            if (block is null || !Text(block.Id, identifier: true) || !Text(block.DisplayName) || !Text(block.Kind) || !Text(block.Source) ||
                !Text(block.Join) || !Text(block.Side) || !Text(block.OrderKind) || !Text(block.SizeMode) || !Text(block.MinimumOrderPolicy))
            {
                return false;
            }

            Push(block.Amount);
            Push(block.Quantity);
            Push(block.Price);
            Push(block.Tag);

            foreach (StrategyCheckInternal check in block.Checks ?? [])
            {
                if (++checkCount > StrategyAuthoringLimits.Checks || check is null || !Text(check.Type) || !Text(check.Operation) || !Text(check.Side) ||
                    !Text(check.OrderKind) || !Text(check.SizeMode) || !Text(check.MinimumOrderPolicy))
                {
                    return false;
                }

                Push(check.Tag);

                if ((check.Operands?.Count ?? 0) + expressions.Count > StrategyAuthoringLimits.ExpressionNodes)
                {
                    return false;
                }

                foreach ((string key, ValueExpressionInternal operand) in check.Operands ?? new Dictionary<string, ValueExpressionInternal>())
                {
                    if (!Text(key, identifier: true))
                    {
                        return false;
                    }

                    Push(operand);
                }
            }
        }

        foreach (StrategyWireInternal wire in definition.Wires ?? [])
        {
            if (wire is null || !Text(wire.To, identifier: true) || !Text(wire.From?.Block, identifier: true) || !Text(wire.From?.Exit, identifier: true))
            {
                return false;
            }
        }

        var count = 0;

        while (expressions.TryPop(out ValueExpressionInternal? expression))
        {
            if (++count > StrategyAuthoringLimits.ExpressionNodes || !Text(expression.Kind) || !Text(expression.Text) || !Text(expression.Name, identifier: true) ||
                !Text(expression.Field) || !Text(expression.Operation))
            {
                return false;
            }

            if ((expression.Arguments?.Count ?? 0) + expressions.Count + count > StrategyAuthoringLimits.ExpressionNodes)
            {
                return false;
            }

            foreach ((string key, ValueExpressionInternal operand) in expression.Arguments ?? new Dictionary<string, ValueExpressionInternal>())
            {
                if (!Text(key, identifier: true))
                {
                    return false;
                }

                Push(operand);
            }
        }

        return true;
    }

    private static bool IsWellFormed(ValueExpressionInternal? expression)
    {
        var pending = new Stack<(ValueExpressionInternal? Expression, int Depth)>();
        pending.Push((expression, 1));
        var count = 0;

        while (pending.TryPop(out (ValueExpressionInternal? Expression, int Depth) item))
        {
            ValueExpressionInternal? value = item.Expression;

            if (++count > StrategyAuthoringLimits.ExpressionNodes || item.Depth > StrategyAuthoringLimits.InlineDepth || value is null ||
                string.IsNullOrWhiteSpace(value.Kind) || value.Kind.Length > StrategyAuthoringLimits.TextLength ||
                value.Text?.Length > StrategyAuthoringLimits.TextLength || value.Name?.Length > StrategyAuthoringLimits.IdentifierLength ||
                value.Field?.Length > StrategyAuthoringLimits.TextLength || value.Operation?.Length > StrategyAuthoringLimits.TextLength ||
                (value.ValueType.HasValue && !Enum.IsDefined(value.ValueType.Value)))
            {
                return false;
            }

            if (value.Arguments is not null)
            {
                if (value.Arguments.Count + pending.Count + count > StrategyAuthoringLimits.ExpressionNodes)
                {
                    return false;
                }

                foreach ((string key, ValueExpressionInternal argument) in value.Arguments)
                {
                    if (string.IsNullOrWhiteSpace(key) || key.Length > StrategyAuthoringLimits.IdentifierLength)
                    {
                        return false;
                    }

                    pending.Push((argument, item.Depth + 1));
                }
            }
        }

        return true;
    }
}