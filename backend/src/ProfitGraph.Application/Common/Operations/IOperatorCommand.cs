namespace ProfitGraph.Application.Common.Operations;

/// <summary>
///     A one-time task the operator runs from a host's console by name (<c>--run {name}</c>), with private input on stdin
///     instead of arguments or configuration (ADR 0022). It runs in its own scope; the host starts no background services
///     and applies no migrations for it. The output never contains secrets.
/// </summary>
public interface IOperatorCommand
{
    string Name { get; }

    /// <summary>
    ///     Returns the process exit code: 0 on success.
    /// </summary>
    Task<int> Run(TextReader input, TextWriter output, CancellationToken cancellationToken);
}