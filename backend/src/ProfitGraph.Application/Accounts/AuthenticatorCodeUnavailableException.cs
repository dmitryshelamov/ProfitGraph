using ProfitGraph.Application.Common.Exceptions;

namespace ProfitGraph.Application.Accounts;

/// <summary>
///     The venue cannot make an authenticator code now, for example without its server time; the API answers 503 (ADR 0022).
/// </summary>
public sealed class AuthenticatorCodeUnavailableException(string message = "The authenticator code is unavailable; try again later.")
    : ApplicationExceptionBase(ExchangeCredentialErrorCodes.AuthenticatorCodeUnavailable, message);