using ProfitGraph.Application.Common.Exceptions;

namespace ProfitGraph.Application.Accounts;

/// <summary>
///     The venue could not list a credential's accounts now; try again later (ADR 0023, TC5).
/// </summary>
public sealed class VenueAccountsUnavailableException(string message = "The venue's accounts are unavailable; try again later.")
    : ApplicationExceptionBase(ExchangeCredentialErrorCodes.AccountsUnavailable, message);