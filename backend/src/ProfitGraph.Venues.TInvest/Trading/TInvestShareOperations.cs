namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     What the account's operations show for the share in a time window (ADR 0030): how many there are, how many of them are
///     not cancelled without any execution, and whether every page was read.
/// </summary>
internal sealed record TInvestShareOperations(int Total, int Executed, bool Complete);
