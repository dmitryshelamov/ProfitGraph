using FluentValidation;

using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.CreateExchangeCredential;

/// <summary>
///     Messages never repeat the submitted values, so a rejected key, secret or field cannot reach a response or a log. A
///     venue takes either an API key and secret or its declared fields (ADR 0023, TC5).
/// </summary>
internal sealed class CreateExchangeCredentialCommandValidator : AbstractValidator<CreateExchangeCredentialCommand>
{
    private const int MaxSecretLength = 512;

    public CreateExchangeCredentialCommandValidator(
        VenueRegistry venues,
        VenuePorts<IVenueCredentialSchema> schemas,
        VenuePorts<IVenueAccountDirectory> directories)
    {
        RuleFor(command => command.Venue)
            .Must(venue => venues.Find(venue) is not null)
            .WithErrorCode("unknown_venue")
            .WithMessage("The venue is not registered.")
            .DependentRules(() => RuleFor(command => command.Venue)
                .Must(venue => IsKeyAndSecret(venue, venues, schemas) || CredentialFieldRules.Declared(venue, venues, schemas, directories) is not null)
                .WithErrorCode(ExchangeCredentialErrorCodes.InputUnsupported)
                .WithMessage("The venue's credentials are not entered on the web form."));
        RuleFor(command => command.Label)
            .Must(label => label?.Trim() is { Length: > 0 and <= ExchangeCredentialLabel.MaxLength })
            .WithErrorCode(AccountRuleException.InvalidCredentialLabel)
            .WithMessage($"A credential label must contain 1 to {ExchangeCredentialLabel.MaxLength} characters.");
        When(
            command => IsKeyAndSecret(command.Venue, venues, schemas),
            () =>
            {
                RuleFor(command => command.ApiKey)
                    .Must(key => IsSecret(key, ApiKeyLast4.Length))
                    .WithErrorCode(AccountRuleException.InvalidApiKey)
                    .WithMessage($"An API key must contain {ApiKeyLast4.Length} to {MaxSecretLength} characters without spaces.");
                RuleFor(command => command.ApiSecret)
                    .Must(secret => IsSecret(secret, minLength: 1))
                    .WithErrorCode("invalid_api_secret")
                    .WithMessage($"An API secret must contain 1 to {MaxSecretLength} characters without spaces.");
            });
        When(
            command => !IsKeyAndSecret(command.Venue, venues, schemas) && CredentialFieldRules.Declared(command.Venue, venues, schemas, directories) is not null,
            () => RuleFor(command => command.Fields)
                .Must((command, fields) => CredentialFieldRules.Matches(fields, CredentialFieldRules.Declared(command.Venue, venues, schemas, directories)!))
                .WithErrorCode("credential_fields_mismatch")
                .WithMessage(
                    $"Give exactly the fields the venue's credentials consist of, each with 1 to {CredentialFieldRules.MaxValueLength} characters without surrounding spaces."));
    }

    private static bool IsKeyAndSecret(string? venue, VenueRegistry venues, VenuePorts<IVenueCredentialSchema> schemas)
    {
        return venues.Find(venue) is { } descriptor && schemas.Find(descriptor.Id)?.Fields is [ExchangeCredentialFields.ApiKey, ExchangeCredentialFields.ApiSecret];
    }

    private static bool IsSecret(string? value, int minLength)
    {
        string? trimmed = value?.Trim();

        return trimmed is not null && trimmed.Length >= minLength && trimmed.Length <= MaxSecretLength && !trimmed.Any(char.IsWhiteSpace);
    }
}