namespace ProfitGraph.Api.Accounts.Contracts;

public sealed record VenueAccountResponse(string Id, string Name, string Kind, bool Eligible, string? Ineligibility);