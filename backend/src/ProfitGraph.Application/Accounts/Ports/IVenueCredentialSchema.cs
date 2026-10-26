using ProfitGraph.Application.MarketData.Ports;

namespace ProfitGraph.Application.Accounts.Ports;

/// <summary>
///     The encrypted fields a venue's credential consists of (ADR 0022, C6). A venue whose fields are exactly an API key
///     and secret accepts credentials from the web form; any other venue provides its own way to create them. A venue
///     without this port takes no credentials.
/// </summary>
public interface IVenueCredentialSchema : IVenuePort
{
    IReadOnlyList<string> Fields { get; }

    /// <summary>
    ///     The one field that holds a non-secret identifier of the account, such as an account number or an API access key; the
    ///     keys page shows it beside the masked key.
    /// </summary>
    string PublicField { get; }
}