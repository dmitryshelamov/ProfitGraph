namespace ProfitGraph.Domain.Common;

/// <summary>
///     Signals a violated domain invariant. Invariants hold even when a caller bypasses the Application pipeline.
/// </summary>
public abstract class DomainException(string message, Exception? innerException = null) : Exception(message, innerException);