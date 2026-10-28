# ADR 0038: Saved Strategies Are Brought Forward to Their Template Automatically

Status: Proposed, 2026-10-28. Supplements ADR 0019, ADR 0025 and ADR 0037; amended by ADR 0040; proven on the test database only. The
owner reviews it in the pull request.

## Context

A strategy is a saved graph: saving creates an immutable version, and a template is only a starting
point the owner copies from. A bot has no strategy of its own; it runs its strategy's latest saved version and switches to a newer one
before each root (ADR 0019, `BotGraphFollower`). ADR 0037 added the Ladder's reaction to a filled manual order to the template, so a
strategy saved before it keeps the old graph, and its bot ignores the owner's manual orders (the real GAZP bot: 50 shares, an exit for
20). Moving each strategy to a new version by hand is slow and is forgotten; the owner asked for the update to happen by itself.

## Decision

A new version of a saved strategy is made by the system when an upgrade applies to its graph.

- **An upgrade is additive.** `StrategyTemplateUpgrades.WithManualFillRebuild` adds the two blocks and three wires of ADR 0037 (the
  same code the template uses, `LadderManualFillRebuild`) and changes nothing else: parameters, values, existing blocks and wires,
  the template kind and the editor layout stay as they are. It is idempotent: a graph that already has the blocks, or already reacts to
  the tag `manual` through a block of its own, is not touched.
- **It applies only to the shape it attaches to.** The graph must be the Ladder with resume: the blocks `resume`, `cancelExit`,
  `exitConditions`, `exit`, `averageConditions`, `restartEvent` and the wires `cancelExit.completed → exitConditions`,
  `exitConditions.yes → exit`, `exit.submitted → averageConditions` and `restartEvent.yes → entryConditions`. A graph without one of
  them is not given the reaction by this step. A graph the owner changed away from the template is left as it is; a Ladder that
  has the chain but no `resume` (the plain Ladder, an older Ladder SMA) first gets the resume blocks (ADR 0040) and then this
  step. The template kind is not the test: older saved graphs carry another kind for the same shape.
- **The upgraded graph is validated** with the strategy validator and the node registry; one that does not validate is not saved.
- **Who runs it.** `StrategyTemplateUpgradeHost` in the Worker's `live` role, 15 seconds after the start (once; a failed pass is tried again after a minute, up to three passes), sends
  `UpgradeStrategyTemplatesCommand`: for every strategy, in one transaction, it reads the current version, applies the upgrade, and
  saves the next version only if the strategy's current version is still the one it read (an owner's save in the meantime wins and the
  strategy is upgraded at the next start of the Worker). It logs each upgraded strategy and how many bots that should run follow it.
  `Strategies:TemplateUpgrades:Enabled` (default `true`) switches the host off.
- **Bots follow by the existing rule.** The save does not ask for `ConfirmLiveImpact`: the owner asked for the upgrade to run by
  itself, and what it adds changes no order and no size, only what the strategy does when the owner's own order fills. A bot adopts
  the version on its next root through the graph change of ADR 0019 and ADR 0025; if the engine refuses it, the bot keeps its version
  and records the rejection (ADR 0019).

## Consequences

- The Ladder strategies of the owner's running bots (Ladder SMA, "as in Profit Fox", Ladder+) that were saved before ADR 0037 react
  to a filled manual order after the next pass, without any action. Orders the bot already holds are not changed by the upgrade; the
  rebuild happens at the next fill of an owner's order.
- Any later additive upgrade is a function in `StrategyTemplateUpgrades` run by the same host; an upgrade that removes or rewrites
  something needs its own decision and is not made this way.
- The version history of such a strategy gets one new version made by the system, with the same name and layout; its backtests
  and bots' stored versions are unaffected.
- A strategy the owner changed away from the template does not get the reaction; the owner adds the blocks in the editor if wanted.
  The plain Ladder gets it together with the resume blocks (ADR 0040).
- Tested: unit tests of the upgrade (the template's own output without the blocks gets exactly the template's blocks and wires, no
  second upgrade, other shapes untouched) and an integration test on the test database (a saved Ladder with resume gets version 2 once,
  a current one is not changed; since ADR 0040 a plain Ladder gets version 2 too). The host is not run in the tests.
