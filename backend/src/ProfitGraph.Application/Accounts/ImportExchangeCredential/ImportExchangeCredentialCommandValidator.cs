using FluentValidation;

using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.ImportExchangeCredential;

/// <summary>
///     Messages name fields only, never their values.
/// </summary>
internal sealed class ImportExchangeCredentialCommandValidator : AbstractValidator<ImportExchangeCredentialCommand>
{
    private const int MaxValueLength = 4096;

    public ImportExchangeCredentialCommandValidator(VenueRegistry venues, VenuePorts<IVenueCredentialSchema> schemas)
    {
        RuleFor(command => command.Venue)
            .Must(venue => venues.Find(venue) is not null)
            .WithErrorCode("unknown_venue")
            .WithMessage("The venue is not registered.");
        RuleFor(command => command.Label)
            .Must(label => label?.Trim() is { Length: > 0 and <= ExchangeCredentialLabel.MaxLength })
            .WithErrorCode(AccountRuleException.InvalidCredentialLabel)
            .WithMessage($"A credential label must contain 1 to {ExchangeCredentialLabel.MaxLength} characters.");
        RuleFor(command => command.PublicHint)
            .Must(hint => hint is { Length: ApiKeyLast4.Length } && !hint.Any(char.IsWhiteSpace))
            .WithErrorCode(AccountRuleException.InvalidApiKey)
            .WithMessage($"The public hint has exactly {ApiKeyLast4.Length} characters without spaces.");
        RuleFor(command => command.Fields)
            .Must((command, fields) => venues.Find(command.Venue) is { } venue && schemas.Find(venue.Id)?.Fields is { } declared &&
                declared.Order(StringComparer.Ordinal).SequenceEqual(fields.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            .WithErrorCode("credential_fields_mismatch")
            .WithMessage("Give exactly the fields the venue's credentials consist of.")
            .Must(fields => fields.Values.All(value => value is { Length: > 0 and <= MaxValueLength } && value.Trim() == value))
            .WithErrorCode("invalid_credential_value")
            .WithMessage($"Every value needs 1 to {MaxValueLength} characters without surrounding spaces.");
    }
}