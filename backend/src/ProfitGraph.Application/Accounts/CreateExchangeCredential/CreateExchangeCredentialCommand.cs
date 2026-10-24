using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Accounts.CreateExchangeCredential;

public sealed record CreateExchangeCredentialCommand(Guid OwnerId, string? Venue, string? Label, string? ApiKey, string? ApiSecret)
    : ICommand<ExchangeCredentialInternal>
{
    public override string ToString()
    {
        return $"CreateExchangeCredentialCommand {{ OwnerId = {OwnerId}, Venue = {Venue}, Redacted }}";
    }
}