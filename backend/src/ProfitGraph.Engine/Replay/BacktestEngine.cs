namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Stateless deterministic replay driver; every invocation owns its account, indicators, identities and ledger.
/// </summary>
public sealed class BacktestEngine
{
    public BacktestResult Run(
        BacktestInput input,
        IBacktestObserver? observer = null,
        CancellationToken cancellationToken = default,
        BacktestRunOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        return new BacktestRunState(input, observer, cancellationToken, options ?? new BacktestRunOptions()).Run();
    }
}