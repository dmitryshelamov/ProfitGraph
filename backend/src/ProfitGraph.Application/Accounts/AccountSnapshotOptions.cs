namespace ProfitGraph.Application.Accounts;

/// <summary>
///     Section <c>Accounts:Snapshots</c>: how often the Worker's <c>live</c> role looks for due account reads and thins the
///     account history (Profit Fox Designer ADR 0069).
/// </summary>
public sealed class AccountSnapshotOptions
{
    public const string SectionName = "Accounts:Snapshots";

    /// <summary>
    ///     How often credentials due for an account read are looked for.
    /// </summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     How often the history is thinned.
    /// </summary>
    public TimeSpan ThinInterval { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    ///     How long the history keeps every snapshot; older snapshots are thinned to hourly.
    /// </summary>
    public TimeSpan FullDetail { get; init; } = TimeSpan.FromDays(7);

    public bool IsValid()
    {
        return PollInterval > TimeSpan.Zero && ThinInterval > TimeSpan.Zero && FullDetail > TimeSpan.Zero;
    }
}