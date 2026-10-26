using FluentValidation;

using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.RenameBot;

internal sealed class RenameBotCommandValidator : AbstractValidator<RenameBotCommand>
{
    public RenameBotCommandValidator()
    {
        RuleFor(command => command.Name)
            .Must(name => name is null || name.Trim().Length <= BotName.MaxLength)
            .WithErrorCode(BotRuleException.InvalidName)
            .WithMessage($"A bot name must contain 1 to {BotName.MaxLength} characters.");
    }
}