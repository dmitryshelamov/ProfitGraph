namespace ProfitGraph.Application.Scanner.Contracts;

public sealed record ScanTradeSettingsInternal(string Venue, Guid CredentialId, decimal Risk, decimal MaxAmount, decimal? Leverage = null);