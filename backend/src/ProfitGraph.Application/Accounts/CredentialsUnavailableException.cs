using ProfitGraph.Application.Common.Exceptions;

namespace ProfitGraph.Application.Accounts;

/// <summary>
///     No master key can encrypt or decrypt credentials, so credential use cases cannot run; the API answers 503.
/// </summary>
public sealed class CredentialsUnavailableException()
    : ApplicationExceptionBase(ExchangeCredentialErrorCodes.Unavailable, "Exchange credential storage is not configured.");