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
}