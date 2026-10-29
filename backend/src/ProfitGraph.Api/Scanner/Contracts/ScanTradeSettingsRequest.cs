namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     How trade cards trade on the venue (ADR 0060): the owner's credential, the quote a stop may lose and the most quote a trade may
///     spend, as exact decimal strings.
/// </summary>
public sealed record ScanTradeSettingsRequest(Guid CredentialId, string Risk, string MaxAmount);