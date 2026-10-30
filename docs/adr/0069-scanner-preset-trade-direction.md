# ADR 0069: A Preset Sets the Direction of Its Trade Cards

Status: Proposed, 2026-10-30. The owner chose to trade with the move the scanner finds, a rise long and a fall short, through a
setting of the preset rather than a separate scanner ("это будет отдельный тип сканера?", "давай четвертый", "да делаем все").
The way it reaches the pair's page is the analyst's proposal. Builds on ADR 0059 (section 4), ADR 0060, ADR 0061 and ADR 0068.

## Context

A pair's page shows every trade card of the pair, long and short where the venue allows shorts (ADR 0061), whichever preset found
it. A short against a fresh rise with its stop at the nearest resistance is a bet on a reversal, not the signal the preset gave; the
owner wants the cards to follow the signal. The page did not know which preset found the pair: the trade ideas are read by venue and
symbol only, and a built-in preset has no identifier.

## Decision

1. **A preset's trade direction.** `ScanPreset.TradeDirection` is `Both`, `Long` or `Short`, set with the name, conditions and sort;
   a new preset trades both ways. It is stored in the preset's JSON definition; a definition stored before it has none and trades
   both ways, so no upgrade of stored presets is needed. The API's preset request takes `tradeDirection` (`Both` when left out) and
   its response returns it; the preset editor offers «Карточки сделок»: «В обе стороны», «Только лонг», «Только шорт».
2. **Built-in presets.** «Импульс» and «Ранний импульс» trade long, «Падение» and «Раннее падение» short, «Под лесенку» both ways, on
   every venue. A copy keeps the direction of the preset it copies.
3. **The pair's page opens on the preset's side.** A row of the scanner table links to its pair with `?trades=Long` or
   `?trades=Short` when the preset shown trades one way; the page keeps it when the chart's timeframe changes. The trade cards'
   direction filter starts on that side, so the cards of the other side are hidden but one click away («Все»). A pair opened without
   it, or from a preset that trades both ways, shows every card as before.
4. **Nothing else changes.** The API still returns every proposal; creating a trade is not limited by the preset, since the owner
   may still take a card of the other side on purpose. On a venue without shorts a short preset finds and alerts on falling pairs,
   and its pairs have no cards.

## Alternatives

- **Filtering the ideas in the API by a preset identifier.** Exact, but a built-in preset has no identifier and the cards would
  need a second read to show the other side.
- **A separate scanner or venue mode for shorts.** The owner asked whether one is needed; the same conditions, sort and alerts
  serve both sides, so a preset setting is enough.
- **Refusing a card of the other side.** It would stop a deliberate trade against the preset; the direction is a default, not a rule.

## Consequences

- The address carries the side, so a pair's page shared or reloaded from the table keeps it.
- Telegram alerts still name the preset only; the alert's cards (ADR 0060, next steps) can take the preset's direction later.
