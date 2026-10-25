namespace ProfitGraph.Venues.TInvest.Accounts;

/// <summary>
///     The encrypted fields of a T-Invest credential (ADR 0023): the Invest API token and the brokerage account it trades.
/// </summary>
internal static class TInvestCredentialFields
{
    public const string Token = "token";
    public const string AccountId = "accountId";
}