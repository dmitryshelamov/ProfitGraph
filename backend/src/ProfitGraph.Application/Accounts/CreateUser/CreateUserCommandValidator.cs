using FluentValidation;

namespace ProfitGraph.Application.Accounts.CreateUser;

internal sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(command => command.Login).ValidLogin();
        RuleFor(command => command.Password).StrongPassword();
        RuleFor(command => command.Role).IsInEnum().WithErrorCode("invalid_role");
    }
}