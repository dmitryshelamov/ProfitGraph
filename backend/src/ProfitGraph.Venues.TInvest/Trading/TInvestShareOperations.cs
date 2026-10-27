namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     What the account's operations show for the share in a time window (ADR 0030): how many there are, how many of them are
///     neither cancelled without any execution nor explained as the bot's own earlier trades, the engine orders (one entry per
///     operation) whose trades explained the others (ADR 0033), and whether every page was read.
/// </summary>
internal sealed record TInvestShareOperations(int Total, int Executed, bool Complete, IReadOnlyList<long> OwnOrders);
