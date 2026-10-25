using FluentValidation;

using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.CreateExchangeCredential;

/// <summary>
///     Messages never repeat the submitted values, so a rejected key or secret cannot reach a response or a log.
/// </summary>
internal sealed class CreateExchangeCredentialCommandValidator : AbstractValidator<CreateExchangeCredentialCommand>
{
    private const int MaxSecretLength = 512;

    public CreateExchangeCredentialCommandValidator(VenueRegistry venues, VenuePorts<IVenueCredentialSchema> schemas)
    {
        RuleFor(command => command.Venue)
            .Must(venue => venues.Find(venue) is not null)
            .WithErrorCode("unknown_venue")
            .WithMessage("The venue is not registered.")
            .DependentRules(() => RuleFor(command => command.Venue)
                .Must(venue => schemas.Find(venues.Find(venue)!.Id)?.Fields is [ExchangeCredentialFields.ApiKey, ExchangeCredentialFields.ApiSecret])
                .WithErrorCode(ExchangeCredentialErrorCodes.InputUnsupported)
                .WithMessage("The venue's credentials are not entered as an API key and secret."));
        RuleFor(command => command.Label)
            .Must(label => label?.Trim() is { Length: > 0 and <= ExchangeCredentialLabel.MaxLength })
            .WithErrorCode(AccountRuleException.InvalidCredentialLabel)
            .WithMessage($"A credential label must contain 1 to {ExchangeCredentialLabel.MaxLength} characters.");
        RuleFor(command => command.ApiKey)
            .Must(key => IsSecret(key, ApiKeyLast4.Length))
            .WithErrorCode(AccountRuleException.InvalidApiKey)
            .WithMessage($"An API key must contain {ApiKeyLast4.Length} to {MaxSecretLength} characters without spaces.");
        RuleFor(command => command.ApiSecret)
            .Must(secret => IsSecret(secret, minLength: 1))
            .WithErrorCode("invalid_api_secret")
            .WithMessage($"An API secret must contain 1 to {MaxSecretLength} characters without spaces.");
    }

    private static bool IsSecret(string? value, int minLength)
    {
        string? trimmed = value?.Trim();

        return trimmed is not null && trimmed.Length >= minLength && trimmed.Length <= MaxSecretLength && !trimmed.Any(char.IsWhiteSpace);
    }
}