using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Accounts.CreateExchangeCredential;

/// <summary>
///     Creates a credential from the web form: an API key and secret for a venue that declares exactly those, or the
///     venue's declared <see cref="Fields" /> for a venue whose credential selects an account (ADR 0023, TC5). It runs
///     outside a transaction because the venue may be asked for the accounts first. Values are never printed.
/// </summary>
public sealed record CreateExchangeCredentialCommand(
    Guid OwnerId,
    string? Venue,
    string? Label,
    string? ApiKey,
    string? ApiSecret,
    IReadOnlyDictionary<string, string>? Fields = null) : ICommand<ExchangeCredentialInternal>, INonTransactionalCommand
{
    public override string ToString()
    {
        return $"CreateExchangeCredentialCommand {{ OwnerId = {OwnerId}, Venue = {Venue}, Redacted }}";
    }
}