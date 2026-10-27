# ADR 0040: Ladders Saved Without the Resume Blocks Get Them

Status: Proposed, 2026-10-28. Supplements ADR 0035, ADR 0038 and ADR 0039, and replaces ADR 0038's rule that a Ladder without resume is
left as it is; proven on unit and integration tests only. The owner reviews it in the pull request.

## Context

The runtime resumes a bot's strategy after a start, after a reconciliation that changed its orders (ADR 0035) and after an order the
venue cancelled without being asked (ADR 0039); the strategy decides what to place. The Ladder restores its exit on resume
(`resume → resumeExitGuard → exitConditions`) only in the revisions that have those blocks. The Bybit bot KASUSDT runs "Ladder SMA"
version 1, saved before they existed: it has no `resume` block. The owner cancelled its exit order on the exchange (2026-10-27); the
engine ended the order as cancelled, the resume found nothing in the graph to run, and the bot kept 832 KAS with only its averaging
order. ADR 0038 left such a graph alone ("not the Ladder with resume"), so its upgrade did not reach it either.

## Decision

`StrategyTemplateUpgrades.Upgrade` brings a saved Ladder graph to what the template of today has, in two additive steps, each only when
the graph has the Ladder's chain (`entryConditions`, `cancelExit`, `exitConditions`, `exit`, `averageConditions`, `restartEvent`
and their wires) and lacks what the step adds:

1. **Restoring at resume** (`LadderResumeRestore`, new): the blocks `resume` (`OnResume`) and `resumeExitGuard` ("base balance and no
   exit order") and the wires `resume → entryConditions`, `resume → resumeExitGuard`, `resumeExitGuard.yes → exitConditions`, the same
   the resume-capable template builds.
2. **The reaction to a filled manual order** (`LadderManualFillRebuild`, ADR 0037/0038), as before, now also for a graph that got step 1.

`UpgradeStrategyTemplatesCommand` (the Worker's host, ADR 0038) uses `Upgrade`; the graph is validated and saved as the next version, the
template kind, parameters and values unchanged. A running bot follows the new version on its next root (ADR 0019).

The catalog's plain "Ladder" template is the Ladder with the same blocks added (`StrategyTemplateUpgrades.Upgrade` on the plain
Ladder, its template kind kept), so a strategy made from any Ladder entry of the catalog has them. The other templates (SMA Crossover,
"Откуп капитуляции", "Импульс на объёме", DCA) evaluate their entry and exit conditions at every candle close, so an order the venue
cancelled comes back at the next close when its conditions hold, and they have no grid to restore; they are not changed.

## Consequences

- A Ladder saved without the resume blocks (the plain Ladder, an older Ladder SMA) now places a missing exit after a start, after a
  reconciliation and after an order the venue cancelled, and starts a cycle at resume when the position is flat, as the current template
  does. This changes what such a strategy does at a restart, which the owner asked for (Profit Fox restores the grid whenever it finds it
  short).
- A graph that is not the Ladder's chain, or that the owner changed away from it, is still left alone.
- The sizes and the prices of the orders do not change; the averaging order comes back at the next candle close (Ladder SMA) or after
  the next exit placement.
- Tested: `StrategyTemplateUpgradesTests` (a Ladder without the resume blocks gets exactly the template's, once; the plain Ladder gets the
  resume-capable template's blocks and wires; a graph without the exit is left alone) and `StrategyTemplateUpgradeTests` on the test
  database. The real KASUSDT graph has the chain and the four wires. Not tested against a live Bybit.
