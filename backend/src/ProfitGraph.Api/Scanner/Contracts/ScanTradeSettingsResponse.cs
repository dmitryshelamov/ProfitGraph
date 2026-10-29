namespace ProfitGraph.Api.Scanner.Contracts;

public sealed record ScanTradeSettingsResponse(string Venue, Guid CredentialId, string Risk, string MaxAmount);