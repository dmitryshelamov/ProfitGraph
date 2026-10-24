using ProfitGraph.Application.Common.Exceptions;

namespace ProfitGraph.Application.Optimization;

/// <summary>
///     An optimization that cannot continue, with a stable code from <see cref="OptimizationErrorCodes" />.
/// </summary>
public sealed class OptimizationException(string code, string message) : ApplicationExceptionBase(code, message);