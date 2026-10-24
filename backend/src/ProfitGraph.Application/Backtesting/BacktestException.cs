using ProfitGraph.Application.Common.Exceptions;

namespace ProfitGraph.Application.Backtesting;

/// <summary>
///     A run that cannot continue, with a stable code from <see cref="BacktestErrorCodes" />.
/// </summary>
public sealed class BacktestException(string code, string message) : ApplicationExceptionBase(code, message);