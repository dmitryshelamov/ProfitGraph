using ProfitGraph.Application.Common.Exceptions;

namespace ProfitGraph.Application.Bots;

/// <summary>
///     The venue's fee rates could not be read, so a bot cannot be created or opened with exact rates; try again later.
/// </summary>
public sealed class VenueFeeRatesUnavailableException(string message = "The venue's fee rates are unavailable; try again later.")
    : ApplicationExceptionBase(BotErrorCodes.FeeRatesUnavailable, message);