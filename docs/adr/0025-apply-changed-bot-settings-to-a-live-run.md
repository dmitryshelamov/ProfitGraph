# ADR 0025: Apply Changed Bot Settings to a Live Run

Status: Proposed, 2026-10-26. No code until the owner accepts it; the owner's answers to the questions at the end amend it.

## Context

A bot's strategy values (`parameters`) and its account capital (`allocation_cap`) may be changed only while it is stopped
(ADR 0016, bot API). The change is stored on the bot. A bot that holds a position keeps its run (checkpoint, ADR 0016), and
that run does not take the new settings:

- The engine opens a stored run with the values it applied when the run started (`BotCheckpoint.AppliedParameters`), not the
  bot's current ones (`BotEngineSessionFactory`).
- The account's capital lives in the restored checkpoint (`StartingCapital`, `QuoteTotal`). `bot.AllocationCap` only sizes a
  run that starts from nothing.
- ADR 0016's text says that with a stored run the edited graph "must compile, because the next start applies it as a graph
  change". Nothing applies it: `BotGraphFollower` compares strategy version identifiers only, and `ChangeGraph` compiles
  with `session.AppliedParameters`.

Found on the dev bot on 2026-10-26: the owner raised the strategy values (`allocationCap` 6000, `buyAmount` 600) and the
per-order limit, stopped and started the bot, and no averaging buy appeared. The checkpoint still applied `allocationCap`
1200; the run had 114 of 1200 in cash after 1086 was spent, so the averaging conditions (cash and remaining allocation at
least `buyAmount`) could not hold. Two more facts were mixed up in that session: the *strategy* value `allocationCap`
(the remaining-allocation check inside the graph) and the *bot's* allocation (the account's money) are different things, and
the owner raised only the first. And the Ladder's averaging block runs only after an exit is submitted, so nothing re-runs it
after a restart.

The result is that a bot with a position can be neither given more money nor retuned except by closing the position, which
the owner cannot always do (Steam items are locked for days).

## Decision

### 1. When a stored run takes the bot's settings

A stored run applies the bot's current values and allocation **once, when its runner opens it after a start**, as a settings
change committed as one root. Settings can change only while the bot is stopped, so "the bot's values differ from the run's
applied values" always means "the owner changed them since the run last ran". There is no other trigger and nothing happens
while the bot runs.

Order of steps in one commit (the checkpoint, the new applied values and the intents are saved together, as for any root):

1. Compile the bot's strategy version with the bot's current values. A value set that does not compile is a refusal (below).
2. Restore the engine as today on the run's old values.
3. **Capital.** Add `delta = bot.AllocationCap - checkpoint.StartingCapital` to the account's `StartingCapital` and
   `QuoteTotal` (an increase adds cash; a decrease removes it and is allowed only while `delta` is covered by free quote,
   which the existing `allocation_below_committed` rule already implies: cap at least cost basis plus reserved quote).
   Realized profit, cost basis, fees and fills are untouched.
4. **Graph.** Switch the engine to the same version compiled with the new values (the engine's existing `ChangeGraph`:
   unchanged indicators keep their state, `Resume(GraphChanged)` is drained once).
5. Record the new values as the run's applied values, so every later version follow (ADR 0019) compiles with them.
6. The start's rules stay: reasons, diagnostics, the restart streak and the daily-loss baseline are cleared (so the added
   money does not read as a profit).

### 2. Orders that are already placed

Placed orders keep the price and size they were placed with; changing a value does not cancel or amend them. The strategy's
next decisions use the new values. The owner who wants an order re-priced cancels it (or the strategy replaces it on its
next trigger). This avoids silent order churn on a live account and matches ADR 0019, which cancels only the orders of
removed blocks.

### 3. Refusal

If the engine or the compiler refuses the new values, the run keeps its old values and capital, the bot gains the
`GraphChangeRejected` diagnostic and its owner one alert (ADR 0019); the bot is not degraded. The rejection is recorded
once for that set of values, so a restart does not retry it until the owner changes them again.

### 4. What re-evaluates after the change

The `Resume(GraphChanged)` is the strategy's hook. A graph with an `OnResume` block reacts, as the "Ladder v2" template's
exit restoration does. The ladder's averaging block has no such trigger, so new money would still wait for the next exit.
Adding `OnResume -> averageConditions` to the Ladder v2 template is a template change and is a question below; an owner's
own strategy can add that wire in the editor at once.

### 5. Visibility

`GET /api/bots/{id}` shows the values the run applies (`appliedParameters`, `appliedAllocationCap`), and the bot page marks
a setting that differs ("применится при следующем старте"). The feed records `SettingsApplied` with the old and new values
when the change is committed, or `GraphChangeRejected` when it is refused.

### 6. Backtests and determinism

A live-only change. The engine, replay and backtests are untouched, and replay hashes are unchanged. The capital step is a
new method on the externally executed account used only by the live runner.

## Stages

1. **Engine and runtime.** `ExternallyExecutedAccount.AdjustCapital`, the settings change in `BotEngineSession` and the
   session factory (detect, compile, adjust, change graph, commit), the refusal path with its alert, feed events. Domain and
   engine unit tests for capital adjustment (increase, decrease within free quote, below committed refused); integration tests
   on the scripted venue: a run with a position takes a new allocation and values after stop and start, the averaging order
   appears through an `OnResume` wire, a refused value set leaves the run as it was, a crash between the steps changes nothing.
2. **API and UI.** `appliedParameters` and `appliedAllocationCap` on the bot, the marker on the form and the page.
3. **Template** (only if the owner agrees): the averaging wire in Ladder v2.

## Consequences

- The owner can add money to, or retune, a bot that holds a position without closing it.
- `StartingCapital` changes during a run, so return figures that divide by it mix capital added later; capital added is
  recorded in the feed so the history stays readable.
- A bad value set cannot take the bot down: it is refused and the bot keeps running on what it had.

## Risks

- **Capital adjustment on a live account** is new engine behavior; a bug could mis-state cash and let the bot place orders
  larger than the real balance. Mitigations: the venue's balance and the per-order limit still gate every order, an increase
  only, plus the refusal paths, are tested first; a decrease is allowed only within free quote.
- **Daily loss.** Adding capital raises equity; the baseline is cleared at the start as for any start, so the added money
  is not counted as profit, and a decrease cannot read as a loss.
- **Two "allocation" settings** (the strategy's `allocationCap` and the bot's allocation) stay confusing; the UI should label
  them, which is a separate change.
- **Stale placed orders** after a change (an exit at the old `rise`) are left to the owner, as decided above.

## Questions for the owner

1. Do you accept that a stored run takes the bot's values and allocation once, on the start after the change, as described?
2. Should orders that are already placed be left as they are (proposed), or should an order whose price comes from a changed
   value be cancelled so the strategy places it again?
3. Is a decrease of the bot's allocation allowed (down to cost basis plus reserved quote, as the API already requires), or only
   an increase?
4. Should the averaging block of the Ladder v2 template also run on `OnResume` (so a settings change or a restart places a
   missing averaging order)? This changes a template; the alternative is that you add the wire in your own strategy.
5. Should the bot's allocation and the strategy's `allocationCap` be one setting, with the strategy value derived from the
   bot's allocation, instead of two the owner must keep in step?
