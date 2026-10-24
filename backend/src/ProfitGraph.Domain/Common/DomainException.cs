namespace ProfitGraph.Domain.Common;

/// <summary>
///     Signals a violated domain invariant. Invariants hold even when a caller bypasses the Application pipeline.
///     The stable <see cref="Code" /> reaches API clients unchanged.
/// </summary>
public abstract class DomainException(string code, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}