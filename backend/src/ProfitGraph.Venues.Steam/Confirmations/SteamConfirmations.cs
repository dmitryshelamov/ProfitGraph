using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.Steam.Orders;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Venues.Steam.Confirmations;

/// <summary>
///     The confirmation step of placing (ADR 0022). For each buy, and each listed asset of a sell, awaiting its mobile
///     confirmation, it approves only the one confirmation whose type and creator match it, recording the approval before
///     and after it is sent, then finalizes an approved buy or marks the asset listed. An approval puts the order on the
///     market, so it is sent only while the core releases the order's bot. A confirmation that matches no entry, or one of
///     several candidates, is never touched; an approval whose answer is unclear leaves the entry unknown, so a restart never
///     approves twice. The account's list is read only while an entry waits.
/// </summary>
internal sealed class SteamConfirmations(
    ISteamOrderJournal journal,
    SteamAccountSessions sessions,
    SteamConfirmationClient client,
    SteamBuyOrders buys,
    SteamSellSettlement sells,
    ISteamOrderWaiting waiting,
    TimeProvider clock)
{
    /// <summary>
    ///     One round for an account. True while an entry still waits for its confirmation, so the caller asks again after
    ///     its backoff.
    /// </summary>
    public async Task<bool> Advance(Guid ownerId, Guid credentialId, CancellationToken cancellationToken)
    {
        IReadOnlyList<SteamOrderJournalEntry> open = await journal.Open(credentialId, cancellationToken);

        foreach (SteamOrderJournalEntry approved in open.Where(entry => entry is { State: SteamOrderJournalState.Approved, Side: OrderSide.Buy }))
        {
            await Attempt(() => buys.Finalize(ownerId, approved, cancellationToken));
        }

        SteamOrderJournalEntry[] awaiting =
        [
            .. open.Where(entry => entry.State == SteamOrderJournalState.AwaitingConfirmation && entry.ConfirmationCreator is not null)
        ];
        SteamAssetReservation[] listings =
        [
            .. (await journal.Reserved(credentialId, cancellationToken)).Where(reservation =>
                reservation.State == SteamReservationState.AwaitingConfirmation && reservation.ListingId is not null)
        ];

        if (awaiting.Length == 0 && listings.Length == 0)
        {
            return false;
        }

        SteamAccountSession session = sessions.For(ownerId, credentialId);

        if (await client.List(session, credentialId, cancellationToken) is not { } pending)
        {
            return true;
        }

        foreach (SteamOrderJournalEntry entry in awaiting)
        {
            int type = entry.Side == OrderSide.Buy ? SteamConfirmation.BuyOrderType : SteamConfirmation.ListingType;
            SteamConfirmation[] matching = [.. pending.Where(confirmation => confirmation.Type == type && confirmation.CreatorId == entry.ConfirmationCreator)];

            if (matching.Length == 1 && await waiting.MayRelease(entry.BotId, cancellationToken) &&
                await Approve(session, entry, matching[0], cancellationToken) is { Side: OrderSide.Buy } approved)
            {
                await Attempt(() => buys.Finalize(ownerId, approved, cancellationToken));
            }
        }

        foreach (SteamAssetReservation listing in listings)
        {
            SteamConfirmation[] matching =
            [
                .. pending.Where(confirmation => confirmation.Type == SteamConfirmation.ListingType && confirmation.CreatorId == listing.ListingId)
            ];

            if (matching.Length == 1 && await journal.Get(listing.ClientOrderId, cancellationToken) is { } entry &&
                await waiting.MayRelease(entry.BotId, cancellationToken) && await Approve(session, listing, matching[0], cancellationToken))
            {
                await Attempt(() => sells.Settle(entry, cancellationToken));
            }
        }

        return true;
    }

    private async Task<bool> Approve(SteamAccountSession session, SteamAssetReservation listing, SteamConfirmation confirmation, CancellationToken cancellationToken)
    {
        SteamAssetReservation approving = listing with { State = SteamReservationState.Approving, UpdatedAt = clock.GetUtcNow() };

        if (!await journal.Transition(approving, SteamReservationState.AwaitingConfirmation, cancellationToken))
        {
            return false;
        }

        SteamWriteReceiptKind answer = await client.Approve(session, listing.CredentialId, confirmation, cancellationToken);
        SteamAssetReservation next = approving with
        {
            State = answer switch
            {
                SteamWriteReceiptKind.Accepted => SteamReservationState.Listed,
                SteamWriteReceiptKind.NotSent => SteamReservationState.AwaitingConfirmation,
                _ => SteamReservationState.Unknown
            },
            UpdatedAt = clock.GetUtcNow()
        };

        return await journal.Transition(next, SteamReservationState.Approving, cancellationToken) && next.State == SteamReservationState.Listed;
    }

    private async Task<SteamOrderJournalEntry?> Approve(
        SteamAccountSession session,
        SteamOrderJournalEntry entry,
        SteamConfirmation confirmation,
        CancellationToken cancellationToken)
    {
        SteamOrderJournalEntry approving = entry with { State = SteamOrderJournalState.Approving, UpdatedAt = clock.GetUtcNow() };

        if (!await journal.Transition(approving, SteamOrderJournalState.AwaitingConfirmation, cancellationToken))
        {
            return null;
        }

        SteamWriteReceiptKind answer = await client.Approve(session, entry.CredentialId, confirmation, cancellationToken);
        SteamOrderJournalEntry next = approving with
        {
            State = answer switch
            {
                SteamWriteReceiptKind.Accepted => SteamOrderJournalState.Approved,
                SteamWriteReceiptKind.NotSent => SteamOrderJournalState.AwaitingConfirmation,
                _ => SteamOrderJournalState.Unknown
            },
            UpdatedAt = clock.GetUtcNow()
        };

        return await journal.Transition(next, SteamOrderJournalState.Approving, cancellationToken) && next.State == SteamOrderJournalState.Approved ? next : null;
    }

    private static async Task Attempt(Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (SteamOrderUnresolvedException)
        {
            // The entry moved meanwhile; the next round reads it again.
        }
    }
}