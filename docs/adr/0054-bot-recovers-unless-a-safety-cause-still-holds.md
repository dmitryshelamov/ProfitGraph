# ADR 0054: A Bot Recovers by Itself Unless a Safety Cause Still Holds

Status: Draft, 2026-10-29. The owner asked for a review of every safety reason against the requirement that a bot is
autonomous. Nothing in this draft is implemented; the owner decides each point in the pull request. Supplements ADR 0016,
ADR 0029, ADR 0030 and ADR 0050.

## Context

The owner's requirement: a bot runs without the owner. It stops and waits only for a critical cause; in every other case
it brings itself back to the state it should be in. The test case is an outage of the whole server for a day (a power cut),
after which the host boots and the bots must continue without anyone touching them.

What already holds after such an outage:

- Docker starts on boot, and PostgreSQL, the API and the Worker run with `restart: unless-stopped`
  (`docker-compose.prod.yml`).
- The live role takes the expired leases of bots whose desired state is `Running` under the next epoch, with the submit gate
  closed (ADR 0016).
- A stored run reads the venue's history from its last closed candle to now, warms on the missed candles without
  `CandleClosed` events and drains one `Resume(Restart)` at the latest closed candle (`BotEngineSession`); no decision is made
  in the past.
- Reconciliation applies what the venue did meanwhile, resumes once more when that changed the engine's orders (ADR 0035),
  and only then opens the gate.
- Any failure that is not a safety reason is `RuntimeFailure` and restarts after 30 seconds and then every minute, without a
  limit (ADR 0050). A network that is not up yet, DNS, or a venue that does not answer end here.

What does not: the ten safety reasons of `BotStatusReasons.IsSafety` never clear by themselves. Two narrow exceptions exist
(`Bot.ClearUnresolvedOrder` with the five-minute recheck of ADR 0030, and `Bot.ClearVenueAccountProtection` when Steam's
protection resumes). Two reasons, `IntentFailed` and `InsufficientBalance`, are cleared by a plain `Start` of a bot that
should already run (`BotStatusReasons.IsRestartableByStart`): the owner's action carries no inspection, it only says "try
again".

## Review of the safety reasons

For each reason: where it is raised, how an outage leads to it, whether the bot clears it today, and what it would take.

### `NoGateway`

- **Raised.** `BotRuntimeHost.TryRun` when the process has no order gateway for the bot's venue, because live trading is
  disabled in its configuration.
- **After an outage.** Only if the host came back with another configuration (a lost or changed `.env`, a Worker started
  without the live role's trading switch).
- **Today.** Waits for the owner, also after the configuration is fixed: the owner has to start the bot.
- **Proposal.** Clear it when a runtime with the venue's gateway sees the bot. The cause is the process, not the bot or the
  venue; enabling trading in the configuration is already the owner's explicit act (AGENTS.md), and requiring a second one
  adds nothing.

### `UnknownVenue`

- **Raised.** Nowhere. The enum value, the Telegram text and the frontend text exist, but no code path degrades a bot with it.
- **Proposal.** Remove it, or document it as reserved. Out of the autonomy question.

### `IntentFailed`

- **Raised.** `DispatchOrderIntentCommandHandler.Fail` when an intent fails its tenth delivery
  (`OrderIntentRetrySchedule`: 1, 2, 5, 15, 30 seconds, then every 60 seconds). Every exception that is not a safety
  exception or a blocked submit counts, so an `HttpRequestException`, a timeout or a gRPC `Unavailable` count as much as an
  answer the gateway does not understand.
- **After an outage.** The server outage itself does not count failures: nothing runs. The dangerous case is a partial
  outage while the server is up: the internet or the venue is unreachable for about five minutes (293 seconds of delays)
  while the bot has an intent to deliver. The intent fails and the bot waits for the owner, although nothing about the order
  is unknown that reconciliation would not settle.
- **Today.** Waits for the owner; a plain `Start` re-arms it, and the next reconciliation looks up every failed submit that may
  have left the process (`ReconcileRequests`).
- **Proposal.** Re-arm on the venue's evidence that it answers again, not by time: the recheck host (below) asks the
  gateway for a read-only health answer (`KeyMayTrade` on Bybit, `GetAccounts` on T-Invest, the market page on Steam) and,
  when it succeeds, returns the bot to `Created`. The runtime's own reconciliation then settles the failed intents exactly as
  after the owner's `Start`. Separately, consider not counting failures that prove the venue was not reached (connection
  refused, DNS, TLS handshake) toward the ten, as the unreadable balance already does not count.

### `UnresolvedOrder`

- **Raised.** Reconciliation (`BotReconciler`, each gateway's `Reconcile`), T-Invest's order translation for a cancel nobody
  asked for, and T-Invest's position-coverage guard before closing an expired DAY order (ADR 0031).
- **After an outage.** The most likely reason after a day offline. T-Invest DAY orders expire with their trading day while no
  post-session sweep runs, and T-Invest may no longer answer for a past day's order; a Bybit submit that may have left just
  before the power cut is unknown to Bybit.
- **Today.** Cleared on evidence by ADR 0030 (T-Invest: day ended, nothing executed in operations, position equals the engine;
  Bybit: 24 hours to 6 days old, no execution, base balance equals the engine; Steam: a buy in `Placing` or `Unknown` after a
  day), rechecked every five minutes, and by ADR 0031 for expired DAY orders while the account's position covers its bots.
  Left to the owner: a Bybit order the engine saw accepted that Bybit no longer knows (a contradiction in Bybit's data),
  T-Invest attempts that are `Unknown`, waiting, planned or partly filled, orders older than 10 days (T-Invest) or 6 days
  (Bybit), Steam sells in flight, and any proof where the account holds shares or coins of the owner's own, because the
  balance check is an equality.
- **Proposal.** Keep the owner for contradictions: an order the venue accepted and then forgot is a fact the bot cannot
  explain. Extend the proofs only where an outage alone pushes a case past them:
  - The 10-day and 6-day windows are shorter than an outage the owner may not notice (a holiday). Bybit's window is bounded by
    the 7 days of `/v5/execution/list` without a time range; with explicit `startTime`/`endTime` pages it can go further. To
    be decided per venue.
  - The balance equality fails for every owner who keeps their own coins or shares on the account. An inequality in the
    right direction plus the execution list is a weaker proof; leave the equality unless the owner accepts that.

### `ForeignOrder`

- **Raised.** Reconciliation and the private streams (Bybit `BybitPrivateStreams`, T-Invest `TInvestOrderStreams`, Steam's
  account page) when an order or execution on the bot's symbol is not the bot's and the bot does not allow foreign orders.
- **After an outage.** The owner traded the symbol by hand while the bot was down (the natural thing to do when the bot is
  offline), or another bot of the same credential trades the same symbol. On Bybit the check reads the open orders and
  `/v5/execution/list` without a time range, which is the last 7 days: one manual trade stops the bot at every start for a
  week, although nothing foreign is open anymore.
- **Today.** Waits for the owner. A `Start` does not help while the execution is still in the list; only allowing foreign
  orders or waiting out the 7 days does.
- **Proposal.** Distinguish what is foreign now from what was foreign once:
  - A foreign **open** order on the symbol keeps the bot stopped: two sources would trade the same book.
  - A foreign **execution** stops the bot once, when it is first seen, and is recorded in the feed with its time. It does not
    stop the bot again: on Bybit, reconciliation counts foreign executions only after the later of the bot's last checkpoint
    and the last acknowledged foreign execution.
  - The recheck host clears the reason when no foreign open order is left.
  - Whether two bots of one credential on one symbol are foreign to each other is a separate question; today they are.

### `KeyPermissions`

- **Raised.** Reconciliation. Bybit: `/v5/user/query-api` answers 10003 (invalid key), 10005 (permission denied) or 10010
  (the request's IP is not bound to the key), or the key is read-only, cannot trade spot or can withdraw. T-Invest: the token
  is unauthenticated or denied, or the account is not open with full access. Steam: the authenticator is not `Ready`, or the
  wallet currency differs from the instrument's.
- **After an outage.** A power cut often changes the home connection's public IP. A Bybit key bound to IPs then answers 10010
  and every bot of that key stops with `KeyPermissions`, and stays so after the owner binds the new IP. A key or token that
  expired during the outage ends here too.
- **Today.** Waits for the owner, even after the owner fixed the key or the IP, until the bot is started.
- **Proposal.** The bot cannot fix a key, but it can see that the key was fixed: the recheck host repeats the same read-only
  check (every 5 minutes, later every 30) and clears the reason when it passes. A key that can withdraw keeps failing the
  check, so the guard against it stays intact.

### `UnsupportedFeeAsset`

- **Raised.** `BybitOrderTranslation` when Bybit charges a fee in an asset that is neither the base nor the quote.
- **After an outage.** Not related.
- **Today.** Waits for the owner, and a `Start` repeats it: the same order update fails again.
- **Proposal.** Stays with the owner. It needs a code change (fee conversion) or an account setting (fee discount token off),
  not a retry.

### `InsufficientBalance`

- **Raised.** `DispatchOrderIntentCommandHandler.Reject` when the venue rejects a submit with `InsufficientQuote` or
  `InsufficientBase`.
- **After an outage.** The owner withdrew or spent funds meanwhile, another bot of the account used them, or the price moved
  so a market buy with its fee no longer fits (the case of the T-Invest market-buy budget fix, pull request #197).
- **Today.** Waits for the owner; a plain `Start` re-arms it.
- **Proposal.** Re-arm on evidence: the rejected intent holds the order, so the recheck host reads the venue balance
  (`IAccountBalanceReader`) and clears the reason when the free amount covers that order with its fee. Telegram reports the stop
  once and the recovery once, not every check.

### `HistoryDataCorrection`

- **Raised.** `BotEngineSessionFactory.Open` when the venue's closed history no longer contains the checkpoint's last candle,
  or contains a different one, on a venue that does not restate closed candles (Bybit, T-Invest); and
  `TInvestLiveCandleSource` when T-Invest reports trades in an interval the bot already received without trades.
- **After an outage.** The restore re-reads the checkpoint's last candle a day later instead of seconds later, which gives a
  correction more time to happen.
- **Today.** Waits for the owner. By the code, a `Start` does not help: the next `Open` compares the same stored candle with the
  same corrected history and degrades again, and a traded run cannot be forgotten (`Bot.ForgetUntradedRun`). This needs a test
  to confirm; if it holds, the owner has no way out short of a new bot.
- **Proposal.** Accept a corrected history on every venue as ADR 0029 accepts Steam's, and record it: the checkpoint takes the
  re-read candle, the feed gets `HistoryRestated`, Telegram reports it. ADR 0029's own argument applies to all venues: orders
  and fills come from the checkpoint's account, indicators are rebuilt from closed candles at every restore, and an order placed
  on the old value stays what it was. A missing candle continues from the latest re-read candle before it. The T-Invest live
  check accepts the corrected interval the same way.

### `VenueAccountProtection` (Steam)

- **Raised.** Steam's persisted breaker: CAPTCHA, restrictions, family view, repeated 401/403/429 or a high error ratio stop
  every bot of the account (ADR 0022, C7).
- **After an outage.** A burst of reads when every bot of the account restores at once may meet 429.
- **Today.** Transient reasons resume automatically at most three times in six hours; hard reasons wait for the owner;
  `Bot.ClearVenueAccountProtection` re-arms the bots when the protection resumes.
- **Proposal.** Keep the hard reasons with the owner: a CAPTCHA is never solved or bypassed by the system. For transient
  reasons, replace "three times in six hours, then the owner" with a growing delay without a limit, as ADR 0050 did for
  runtime failures.

### Control reasons (outside the review)

`Killed` and `DailyLossLimit` are the owner's or the risk limit's decision to stop, not a failure, and stay until the next
start. Whether a daily-loss stop should end with the next UTC day is a separate decision for the owner.

## Decision (proposed)

1. **Principle.** A safety reason stops a bot while its cause holds. When the venue's own evidence shows the cause is gone,
   the bot returns by itself, provided the owner's desired state is `Running`, no trading halt is on and the bot was not
   killed. Time alone never clears a reason. A reason stays with the owner only when the bot cannot gather such evidence: a
   contradiction in the venue's data, a code limitation, or a decision only a person may make (a CAPTCHA).
2. **One recheck host.** `UnresolvedOrderRecheckHost` becomes a general safety recheck in the Worker's live role. Every five
   minutes, and every 30 minutes after an hour, it asks a read-only check per reason (`CanClear`). A bot that passes returns to
   `Created`, and its own start does the work. The feed records `Recovered` with the reason; Telegram sends one message for
   the stop and one for the recovery. Checks only read and never open streams or move attempts, as `CanProveAbsence` does today.
3. **Per reason.**

| Reason | After this ADR |
|---|---|
| `NoGateway` | Cleared when a runtime with the venue's gateway sees the bot |
| `UnknownVenue` | Removed (never raised) |
| `IntentFailed` | Cleared when the venue answers a read-only health check; unreachable-venue errors possibly not counted |
| `UnresolvedOrder` | As ADR 0030 and 0031; the proof windows reviewed for long outages |
| `ForeignOrder` | Open foreign orders keep the bot stopped; a past foreign execution stops it once; cleared when nothing foreign is open |
| `KeyPermissions` | Cleared when the same key check passes again |
| `UnsupportedFeeAsset` | Owner |
| `InsufficientBalance` | Cleared when the free balance covers the rejected order with its fee |
| `HistoryDataCorrection` | Not a stop: the corrected history is accepted and recorded on every venue |
| `VenueAccountProtection` | Hard reasons stay with the owner; transient ones resume with a growing delay without a limit |

## Consequences

- After a day-long outage, a bot stops for the owner only for a contradiction it cannot explain, an unsupported fee asset, a
  hard Steam protection, or an order beyond the proofs of ADR 0030. Every other stop ends by itself once its cause is gone, and
  the feed and Telegram show both ends.
- The owner loses a moment of review after a key change, a manual trade on the bot's symbol and a corrected candle. The feed
  and Telegram record each of them instead.
- Every recheck costs a few read requests per stopped bot every five minutes; they go through each venue's request limits.
- Backtests, replay and the engine are untouched.

## Open questions for the owner

1. `HistoryDataCorrection`: accept corrected history on Bybit and T-Invest, or keep the stop and give the owner a way out,
   such as an "accept the venue's history" action?
2. `ForeignOrder`: is a manual trade on the bot's symbol a reason to stop once, or should only open foreign orders count? Are
   two bots of one credential on one symbol foreign to each other?
3. `IntentFailed`: should errors that prove the venue was not reached stop counting toward the ten failures?
4. `UnresolvedOrder`: longer proof windows for long outages, per venue?
5. `DailyLossLimit`: should the stop end with the next UTC day?

## Tests (when implemented)

- Domain: each new clear transition and each refused one (desired state stopped, halt on, killed bot, a reason that is not
  cleared that way).
- Integration on the scripted venue: a bot degraded by each reason returns once its check passes and stays stopped while it
  fails; `HistoryDataCorrection` on a non-restating venue continues and records `HistoryRestated`; a Bybit foreign execution
  older than the checkpoint does not stop a restart.
- A test first that confirms or refutes that a `Start` after `HistoryDataCorrection` degrades the bot again.
- An outage drill on production, with the owner's permission: stop the Worker for some hours while bots hold orders, start it,
  and read each bot's feed.
