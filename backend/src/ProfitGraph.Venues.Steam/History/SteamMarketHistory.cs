using System.Globalization;

using Microsoft.Extensions.Options;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Venues.Steam.History;

/// <summary>
///     Reads an account's new market history (Profit Fox Designer ADR 0087). Pages are read newest first until they reach
///     the stored history less <see cref="SteamOptions.HistoryOverlap" /> events; the read stores its new receipts only when
///     Steam's total is the same on every page and no smaller than what is stored, every overlapped event equals the stored
///     one at its position, and the pages leave no gap. The first read of an account anchors at its newest page: older
///     events are never needed, since nothing was placed before. Any doubt returns null and stores nothing.
/// </summary>
internal sealed class SteamMarketHistory(ISteamMarketHistoryStore store, IOptions<SteamOptions> options)
{
    /// <summary>
    ///     The receipts stored by this read, oldest first; empty when nothing happened, null when history is unknown now.
    /// </summary>
    public async Task<IReadOnlyList<SteamStoredReceipt>?> Read(
        SteamAccountSession session,
        Guid credentialId,
        SteamCurrency currency,
        SellerFeeSchedule fees,
        CancellationToken cancellationToken)
    {
        SteamOptions settings = options.Value;
        long? next = await store.Next(credentialId, cancellationToken);
        var positions = new SortedDictionary<long, SteamMarketReceipt>();
        long? total = null;

        for (var page = 0; page < settings.HistoryMaximumPages; page++)
        {
            long start = (long)page * settings.HistoryPageSize;
            SteamRequestResult result = await session.Execute(
                SteamHttpRequest.Get(
                    SteamEndpoint.Community,
                    string.Create(CultureInfo.InvariantCulture, $"/market/myhistory/render/?start={start}&count={settings.HistoryPageSize}&norender=1&l=english"),
                    SteamRequestClass.History),
                cancellationToken);

            if (!result.IsSuccess || SteamMarketHistoryParser.Page(result.Response!.Body, currency, fees, out _) is not { } parsed)
            {
                return null;
            }

            // A new event shifts the pages, and stored history ahead of Steam cannot be explained: both wait for the next read.
            if (parsed.Start != start || (total is { } known && parsed.Total != known) || parsed.Total < (next ?? 0))
            {
                return null;
            }

            total = parsed.Total;

            for (var index = 0; index < parsed.Receipts.Count; index++)
            {
                positions[parsed.Total - 1 - start - index] = parsed.Receipts[index];
            }

            long lowest = next is { } stored ? Math.Max(val1: 0, stored - settings.HistoryOverlap) : Math.Max(val1: 0, parsed.Total - settings.HistoryPageSize);
            long oldestRead = parsed.Total - start - parsed.Receipts.Count;

            if (oldestRead <= lowest || parsed.Receipts.Count == 0)
            {
                return await Commit(credentialId, next, positions, Math.Max(lowest, oldestRead), parsed.Total, cancellationToken);
            }
        }

        return null;
    }

    private async Task<IReadOnlyList<SteamStoredReceipt>?> Commit(
        Guid credentialId,
        long? next,
        SortedDictionary<long, SteamMarketReceipt> positions,
        long first,
        long total,
        CancellationToken cancellationToken)
    {
        SteamStoredReceipt[] read = [.. positions.Where(pair => pair.Key >= first).Select(pair => new SteamStoredReceipt(pair.Key, pair.Value))];

        if (read.Length != total - first)
        {
            return null;
        }

        if (next is { } stored)
        {
            // The overlap must match what is stored, position by position, before anything newer is believed. Stored history may
            // begin inside the overlap, at the anchor of the account's first read.
            IReadOnlyList<SteamStoredReceipt> known = await store.From(credentialId, first, cancellationToken);
            long from = known.Count > 0 ? known[0].Position : stored;

            if (known.Count != stored - from || known.Any(receipt => !read.Contains(receipt)))
            {
                return null;
            }
        }

        SteamStoredReceipt[] fresh = [.. read.Where(receipt => receipt.Position >= (next ?? first))];

        if (fresh.Length == 0)
        {
            return fresh;
        }

        return await store.Append(credentialId, next, fresh, cancellationToken) ? fresh : null;
    }
}