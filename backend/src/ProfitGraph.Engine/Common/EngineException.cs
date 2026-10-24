namespace ProfitGraph.Engine.Common;

/// <summary>
///     Signals a violated engine invariant or invalid engine input. A run that hits one fails instead of continuing.
/// </summary>
public abstract class EngineException(string message, Exception? innerException = null) : Exception(message, innerException);