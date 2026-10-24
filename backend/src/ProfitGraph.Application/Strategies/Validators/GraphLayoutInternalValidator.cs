using FluentValidation;

using ProfitGraph.Application.Strategies.Contracts;

namespace ProfitGraph.Application.Strategies.Validators;

internal sealed class GraphLayoutInternalValidator : AbstractValidator<GraphLayoutInternal>
{
    public GraphLayoutInternalValidator()
    {
        RuleFor(layout => layout.Positions).NotNull();
        RuleFor(layout => layout.Positions)
            .Must(positions => positions.All(pair =>
                !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null &&
                double.IsFinite(pair.Value.X) && double.IsFinite(pair.Value.Y)))
            .When(layout => layout.Positions is not null);
    }
}