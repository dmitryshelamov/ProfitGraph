using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Common;
using ProfitGraph.Domain.Jobs;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Domain.Scanner;

/// <summary>
///     A replay of a preset's filter over a venue's history (ADR 0059, section 6): the filter is copied when the backtest starts, so
///     later changes to the preset do not change it. A background job runs it and completes it once with its result; until then the
///     job's state is the backtest's.
/// </summary>
public sealed class ScanBacktest : AggregateRoot
{
    public const int MaxStoredSignals = 2000;

    public ScanBacktestId Id { get; }
    public UserId OwnerId { get; }
    public VenueId Venue { get; }
    public ScanPresetName PresetName { get; }
    public ScanFilter Filter { get; }

    /// <summary>
    ///     The half-open UTC period whose minutes are checked: [From, To).
    /// </summary>
    public DateTimeOffset From { get; }

    public DateTimeOffset To { get; }
    public BackgroundJobId JobId { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public ScanBacktestResult? Result { get; private set; }

    private ScanBacktest(
        ScanBacktestId id,
        UserId ownerId,
        VenueId venue,
        ScanPresetName presetName,
        ScanFilter filter,
        DateTimeOffset from,
        DateTimeOffset to,
        BackgroundJobId jobId,
        DateTimeOffset createdAt,
        DateTimeOffset? completedAt,
        ScanBacktestResult? result)
    {
        if (to <= from)
        {
            throw new ArgumentException("A scanner backtest's period ends after it starts.", nameof(to));
        }

        Id = id;
        OwnerId = ownerId;
        Venue = venue;
        PresetName = presetName;
        Filter = filter;
        From = from;
        To = to;
        JobId = jobId;
        CreatedAt = createdAt;
        CompletedAt = completedAt;
        Result = result;
    }

    public static ScanBacktest Start(
        UserId ownerId,
        VenueId venue,
        ScanPresetName presetName,
        ScanFilter filter,
        DateTimeOffset from,
        DateTimeOffset to,
        BackgroundJobId jobId,
        DateTimeOffset now)
    {
        return new ScanBacktest(ScanBacktestId.New(), ownerId, venue, presetName, filter, from, to, jobId, now, completedAt: null, result: null);
    }

    public static ScanBacktest Restore(
        ScanBacktestId id,
        UserId ownerId,
        VenueId venue,
        ScanPresetName presetName,
        ScanFilter filter,
        DateTimeOffset from,
        DateTimeOffset to,
        BackgroundJobId jobId,
        DateTimeOffset createdAt,
        DateTimeOffset? completedAt,
        ScanBacktestResult? result)
    {
        return new ScanBacktest(id, ownerId, venue, presetName, filter, from, to, jobId, createdAt, completedAt, result);
    }

    /// <summary>
    ///     Stores the result once; a backtest that already has one keeps it and answers false.
    /// </summary>
    public bool Complete(ScanBacktestResult result, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (Result is not null)
        {
            return false;
        }

        if (result.Signals.Count > MaxStoredSignals)
        {
            throw new ArgumentException($"A scanner backtest stores at most {MaxStoredSignals} signals.", nameof(result));
        }

        Result = result;
        CompletedAt = now;

        return true;
    }
}