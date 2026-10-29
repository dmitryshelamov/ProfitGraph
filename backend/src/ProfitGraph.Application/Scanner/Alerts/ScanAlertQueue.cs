using System.Globalization;

using ProfitGraph.Application.Common.Numbers;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Notifications;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.Alerts;

/// <summary>
///     Checks a new snapshot against every preset of its venue whose alerts are on (ADR 0059, section 5) and queues one Telegram
///     message per preset with the pairs that entered its result, at most <see cref="MaxNamedPairs" /> named. A row whose candles are
///     still being repaired counts as outside every result. Nothing is queued for an owner without a linked chat that is not blocked,
///     or for an inactive account; the watch still records the check.
/// </summary>
public sealed class ScanAlertQueue(
    IScanAlertWatchRepository watches,
    IScanPresetRepository presets,
    ITelegramLinkRepository links,
    IUserRepository users,
    ITelegramNotificationRepository notifications)
{
    public const int MaxNamedPairs = 10;

    public async Task Check(ScanSnapshotInternal snapshot, CancellationToken cancellationToken)
    {
        foreach (ScanAlertWatch watch in await watches.ListForUpdate(new VenueId(snapshot.Venue), cancellationToken))
        {
            if (await presets.GetForUpdate(watch.PresetId, watch.OwnerId, cancellationToken) is not { } preset)
            {
                continue;
            }

            Dictionary<string, ScanSnapshotRowInternal> kept = snapshot.Rows
                .Where(row => !row.Incomplete && preset.Keeps(row.Metrics))
                .ToDictionary(row => row.Symbol, StringComparer.Ordinal);
            IReadOnlyList<string> entered = watch.Check(kept.Keys, snapshot.ComputedAt);
            await watches.Update(watch, cancellationToken);

            if (entered.Count > 0 && await links.Get(watch.OwnerId, cancellationToken) is { BlockedAt: null } &&
                await users.GetById(watch.OwnerId, cancellationToken) is { IsActive: true })
            {
                await notifications.Add(
                    TelegramNotification.QueueScanAlert(
                        TelegramNotificationId.New(),
                        watch.OwnerId,
                        watch.PresetId,
                        Details(snapshot.Venue, preset, [.. entered.Select(symbol => kept[symbol])]),
                        snapshot.ComputedAt),
                    cancellationToken);
            }
        }
    }

    /// <summary>
    ///     The message's details as invariant strings: the venue, the preset's name, how many pairs entered, and for each named pair its
    ///     symbol and the exact values of the price, the preset's condition metrics and its sort metric.
    /// </summary>
    private static Dictionary<string, string> Details(string venue, ScanPreset preset, IReadOnlyList<ScanSnapshotRowInternal> rows)
    {
        ScanMetric[] shown =
        [
            ScanMetric.Price, .. preset.Conditions.Select(condition => condition.Metric), preset.Sort.Metric, ScanMetric.Support, ScanMetric.Resistance,
            ScanMetric.RewardRisk
        ];
        var details = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["venue"] = venue,
            ["preset"] = preset.Name.Value,
            ["count"] = rows.Count.ToString(CultureInfo.InvariantCulture)
        };

        for (var index = 0; index < Math.Min(rows.Count, MaxNamedPairs); index++)
        {
            ScanMetrics metrics = rows[index].Metrics;
            details[$"symbol.{index}"] = rows[index].Symbol;
            details[$"metrics.{index}"] = string.Join(
                separator: ';',
                shown.Distinct().Where(metric => metrics.Of(metric) is not null).Select(metric => $"{metric}={ExactDecimalText.Format(metrics.Of(metric)!.Value)}"));
        }

        return details;
    }
}