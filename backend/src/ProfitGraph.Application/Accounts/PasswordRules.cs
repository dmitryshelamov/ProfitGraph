using FluentValidation;

using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts;

internal static class PasswordRules
{
    internal static IRuleBuilderOptions<T, string> ValidLogin<T>(this IRuleBuilder<T, string> rule)
    {
        return rule.Must(login => !string.IsNullOrWhiteSpace(login) && login.Trim().Length <= User.MaxLoginLength)
            .WithErrorCode("invalid_login")
            .WithMessage($"A login must contain 1 to {User.MaxLoginLength} characters.");
    }

    internal static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule)
    {
        return rule.Must(password => !string.IsNullOrWhiteSpace(password) && password.Length >= User.MinPasswordLength)
            .WithErrorCode("weak_password")
            .WithMessage($"A password must contain at least {User.MinPasswordLength} characters.");
    }
}