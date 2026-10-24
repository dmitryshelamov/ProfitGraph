using FluentValidation;

namespace ProfitGraph.Application.Accounts.BootstrapOwner;

internal sealed class BootstrapOwnerCommandValidator : AbstractValidator<BootstrapOwnerCommand>
{
    public BootstrapOwnerCommandValidator()
    {
        RuleFor(command => command.Login).ValidLogin();
        RuleFor(command => command.Password).StrongPassword();
    }
}