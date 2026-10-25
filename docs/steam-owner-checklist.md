# Steam owner checklist

What the owner does, in order, to trade a Steam Community Market item with a ProfitGraph bot
([ADR 0022](adr/0022-steam-community-market-model.md)). Every step is the owner's: agents never handle real Steam
credentials, call authenticated Steam endpoints or place, approve or cancel real orders.

## Before the first bot

1. **Switches.** `Venues:Steam:Enabled` turns on every Steam request, market data included; `Live:Trading:Enabled`
   registers the order gateway and its workers on the Worker with the live role. Both are false in every committed
   setting. Set them only in the production environment, together with the official Steam origins
   (`steamcommunity.com`, `login.steampowered.com`, `api.steampowered.com`).
2. **One system per account.** Stop Profit Fox Designer's Steam bots on the account first. Its open buy orders on a
   ProfitGraph bot's item make that bot refuse to start with `ForeignOrder` unless the bot allows foreign orders.
3. **Credential.** Either import the existing Steam Guard with the Worker's `--run steam-import` (private JSON on stdin;
   it prints only the credential ID and the last four digits of the Steam ID), or link a new authenticator from the
   credentials page (sign-in, e-mail code, saving the recovery code, activation). Keep the recovery code offline.
4. **Guard status.** Wait until the credential card shows Guard `Ready`. An imported account is checked within minutes;
   a newly linked authenticator stays `CoolingDown` for about 15 days, as Steam holds new authenticators. `Restricted`
   means Steam does not let the account use the market.
5. **Wallet.** The wallet currency must be the item's (`KZT:730:<market hash name>` trades in tenge); a bot on another
   currency's item refuses to start.

## Creating and starting a bot

6. Create the bot on the item with a `1h` or `1d` timeframe and review its Steam settings: the trade hold in whole
   days, the market buy slippage and the market buy deadline.
7. Start with one item and a small step. A market buy pays the lowest listing plus the slippage and is cancelled for its
   remainder after the deadline.
8. Confirmations are approved by the bot itself, only the one whose type and creator match its order; leave the phone's
   other confirmations to the owner.

## While it runs

9. **Waiting codes** on the bot page explain an order Steam has not taken yet: `SingleBuyOrderSlot` (one buy order per
   item and account; the order nearest the market goes first), `AwaitingConfirmation`, `SettlementHold` with the time
   the items unlock, `RateLimited`.
10. **Fills** come only from market history and the buy order's own counters. The owner's own purchase of the same item
    while a bot buys it attributes nothing, and the bot's order waits for reconciliation.
11. **Protection.** A CAPTCHA, a market restriction or repeated refusals stop every bot of the account and every later
    request (`VenueAccountProtection`). Solve the cause on Steam, then resume the protection on the credential card;
    transient noise resumes by itself a few times.
12. **Unresolved orders.** A write that may have reached Steam without a clear answer is never sent again; the bot degrades
    with `UnresolvedOrder`. Check the account on Steam, then let reconciliation settle it.
13. **Steam Guard code.** The credential card shows the current code to the owner only, for signing in elsewhere.

## Stopping

14. Stopping a bot leaves its orders on Steam as they are, but an order it still holds locally (a waiting buy, an item
    under its hold, an unapproved confirmation) is never sent while it is stopped.
15. The kill switch stops the bot at once, rejects its unsent orders and cancels its open ones: buy orders are cancelled
    and listings removed on Steam. A removed listing's item counts again once market history shows it back, possibly
    under a new asset ID.
