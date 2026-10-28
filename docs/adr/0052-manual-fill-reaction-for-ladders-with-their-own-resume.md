# ADR 0052: Ladders With Their Own Resume Get the Manual-Fill Reaction

Status: Proposed, 2026-10-28. Amends ADR 0038 and ADR 0040. The owner asked for it after a manual buy of a Steam Ladder bot did not
rebuild its grid. Proven on unit tests only.

## Context

ADR 0038 adds the Ladder's reaction to a filled manual order (ADR 0037) to saved strategies, but only to the "Ladder with resume": the
graph had to have a block with the id `resume`. ADR 0040 adds `resume` and `resumeExitGuard` to a Ladder that has no resume event, and
leaves alone a graph with an `OnResume` event of its own under another id. Such a graph therefore got neither upgrade.

The owner's Steam strategy for "CS Dreams & Nightmares Case 1h" (version 3) is that graph: the owner added `onresume_6b61cd8a`
(`OnResume`) and `condition_3b69ebdd` ("base balance and no `ladder.exit`") to the Ladder chain in the editor, which is what the template's
`resume` and `resumeExitGuard` do. On 2026-10-28 the bot's manual buy filled at 496; the bot then held four items, but its exit stayed
sized for three, because nothing reacted to the fill.

The id of the resume event says nothing about the graph's shape. What the reaction attaches to is the Ladder's chain
(`manualCancelAverage.completed → cancelExit` and `→ restartEvent`), and the requirement of a resume event only tells a Ladder that restores
its orders from the plain Ladder, which gets the resume blocks first (ADR 0040).

## Decision

- `StrategyTemplateUpgrades.WithManualFillRebuild` requires the Ladder chain (`entryConditions`, `cancelExit`, `exitConditions`, `exit`,
  `averageConditions`, `restartEvent` and the four wires of ADR 0038) and any `event` block with the source `OnResume`, whatever its id,
  instead of a block with the id `resume`. Everything else is unchanged: the reaction is added only once, never to a graph that reacts
  to the tag `manual` its own way, and the upgraded graph is validated before it is saved.
- `WithResumeRestore` is unchanged: a graph with its own resume event does not get `resume` and `resumeExitGuard`, so the owner's resume
  is not doubled.
- A graph with `resumeExitGuard` and no resume event at all is still left alone.

## Consequences

- At the next start of the Worker, the owner's Steam Ladder gets a new version with `manualFilled` and `manualCancelAverage`, and its bot
  takes it through the graph change of ADR 0019. The graph change drains `Resume(GraphChanged)`, so the owner's own resume places a
  missing exit and averaging order at the switch, as ADR 0040 describes for its step.
- From then on a filled manual order cancels the averaging order and the exit and places them again on the whole position.
- Tested: `StrategyTemplateUpgradesTests` (a Ladder with the resume blocks under the owner's ids gets exactly the template's manual
  reaction, validates and is not upgraded twice; a plain Ladder with its own resume event gets only the manual reaction). Not tested
  against a real venue.
