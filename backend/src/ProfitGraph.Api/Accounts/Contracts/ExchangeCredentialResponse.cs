namespace ProfitGraph.Api.Accounts.Contracts;

public sealed record ExchangeCredentialResponse(Guid Id, string Venue, string Label, string ApiKeyLast4, DateTimeOffset CreatedAt);