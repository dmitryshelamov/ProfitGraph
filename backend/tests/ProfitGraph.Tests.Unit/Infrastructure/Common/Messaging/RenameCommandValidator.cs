using FluentValidation;

namespace ProfitGraph.Tests.Unit.Infrastructure.Common.Messaging;

internal sealed class RenameCommandValidator : AbstractValidator<RenameCommand>
{
    public RenameCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty();
    }
}