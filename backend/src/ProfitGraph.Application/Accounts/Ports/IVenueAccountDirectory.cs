using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.MarketData.Ports;

namespace ProfitGraph.Application.Accounts.Ports;

/// <summary>
///     The accounts a venue credential can select, for a venue whose credential names one of several accounts (ADR 0023,
///     TC5). A venue with this port takes credentials from the web form by its declared fields: the form lists the accounts
///     for the fields without <see cref="AccountField" />, and saving lists them again, so access revoked after the listing is
///     not bypassed. Failures are <c>ApplicationValidationException</c> for refused fields or
///     <c>VenueAccountsUnavailableException</c>; secrets never enter them.
/// </summary>
public interface IVenueAccountDirectory : IVenuePort
{
    /// <summary>
    ///     The credential field that names the chosen account.
    /// </summary>
    string AccountField { get; }

    Task<IReadOnlyList<VenueAccountInternal>> List(IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken);
}