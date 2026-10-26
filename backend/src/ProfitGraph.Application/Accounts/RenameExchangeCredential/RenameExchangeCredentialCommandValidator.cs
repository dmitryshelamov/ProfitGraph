using FluentValidation;

using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.RenameExchangeCredential;

internal sealed class RenameExchangeCredentialCommandValidator : AbstractValidator<RenameExchangeCredentialCommand>
{
    public RenameExchangeCredentialCommandValidator()
    {
        RuleFor(command => command.Label)
            .Must(label => label?.Trim() is { Length: > 0 and <= ExchangeCredentialLabel.MaxLength })
            .WithErrorCode(AccountRuleException.InvalidCredentialLabel)
            .WithMessage($"A credential label must contain 1 to {ExchangeCredentialLabel.MaxLength} characters.");
    }
}