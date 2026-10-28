# ADR 0019: Live Graph Change

Status: Accepted, 2026-10-25.

## Context

Every save of a strategy creates a new version. A live bot runs one version with the parameter values its run applied
(ADR 0016). Profit Fox Designer ADR 0057 let a running bot switch to its strategy's latest version without a restart,
keeping indicator state that did not change, cancelling orders of removed blocks and emitting `Resume(GraphChanged)`.
The ProfitGraph engine already implements the switch (`CausalDispatcher.ChangeGraph`); this ADR decides when a running
bot uses it and how the owner learns about it.

## Decision

- **When.** Before every root, candle or venue update, the runner compares the engine's version with the strategy's
  latest (`BotGraphFollower`). A different version that the bot has not rejected before is a candidate.
- **Candidate.** It takes the run's applied parameter values; the bot's parameters cannot change while it runs. A
  candidate whose parameters no longer match those values, that does not compile, or that the engine refuses is
  rejected once (`RejectBotStrategyVersionCommand`, fenced by the lease): the bot keeps its version, gains the
  `GraphChangeRejected` diagnostic and its owner one alert. A later version is a new candidate.
- **Switch.** The engine warms the candidate on closed history ending at its last candle (the run's own last candle,
  earlier ones from the venue), keeps the state of unchanged indicators, requests a cancel for every order whose block
  is gone and drains one `Resume(GraphChanged)`. The root is committed like any other, with a cancel intent for each
  such order, and the bot records the new version. Only a switch clears an earlier rejection.
- **Live impact.** `GET /api/strategies/{id}/live-impact` lists the strategy's bots that should run. While any exists, a
  save without `confirmLiveImpact: true` answers 409 `live_impact`; the editor shows the bots and saves again with the
  confirmation.
- **Untraded run.** A stopped bot whose run holds and spent nothing (ADR 0016: its orders, if any, all ended without a
  fill) starts afresh on the latest version (in place since stage 5.4, widened since); the switch above is only for
  running bots.

## Consequences

- A switch costs two reads per root and one history read per candidate; a candidate is compiled at most once per
  version while it stays rejected.
- A restart opens the stored checkpoint on the version it ran and switches on the next root, so a version saved while
  the bot was down is applied the same way.

## Amendment: following without waiting for a root

- A running bot used to notice a newer version only before its next root (a candle or a venue update), so a bot with no
  working orders on a venue with hourly candles waited up to an hour after the owner saved.
- Every `Bots:Runtime:GraphCheckInterval` (2 s) the runner reads the strategy's latest version in its own scope
  (`BotGraphFollower.Candidate`: a version the bot does not run and has not rejected) and, once per new version, puts a
  graph-change root on its queue. Before any root the runner follows as before, so the switch, the rejection rules and the
  `Resume(GraphChanged)` are unchanged; the root only makes the switch happen now.
- The check costs two reads per bot every interval and writes nothing unless the bot switches.

## Amendment: parameters of a new version (ADR 0047)

- A candidate no longer needs the run's values to name exactly its parameters: they are fitted to it (a new parameter takes its
  default, a removed one is dropped), and the bot's own values follow in the same commit. See
  [ADR 0047](0047-bot-values-follow-the-strategy-parameters.md).
