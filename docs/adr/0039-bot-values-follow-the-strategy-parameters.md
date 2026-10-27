# ADR 0039: A Bot's Values Follow the Parameters of the Strategy Version It Runs

Status: Proposed, 2026-10-28: the owner chose this option ("вариант А") in chat. Supplements ADR 0019 and ADR 0025. Proven on the
scripted venue and the test database only.

## Context

A strategy version names its parameters; a bot holds a value for each of them and nothing else (`BotParameterRules`, Profit Fox
Designer ADR 0068). A running bot follows the strategy's latest version with the values its run applied (ADR 0019), and values the
owner changes while the bot is stopped are applied once on the next start to the version the run runs (ADR 0025). Nothing let the
two meet when a saved version added or removed a parameter:

1. A running bot on v1 with `{a}` met v2 with `{a, b}`. It had no value for `b`, so it rejected v2 and stayed on v1.
2. The owner stopped it and set `{a, b}`. That passed, because `ChangeBotParameters` checks the values against the latest version.
3. On the start `ApplySettings` compiled v1 with `{a, b}` and refused it (`b` is not a parameter of v1). On the first root `Follow`
   compiled v2 with the old `{a}` and refused it again.

A bot holding a position could reach neither the new version nor the new values without closing the position, and every restart
raised two new `GraphChangeRejected` alerts, because the rejected version alternated between v1 and v2. When both the values and the
version had changed, the start also switched the graph twice (`ApplySettings`, then `Follow`), draining two `Resume(GraphChanged)`.

## Decision

- **Fitting.** A bot's values are fitted to the version it switches to (`BotParameters.Fit`): a parameter the version still has keeps
  its value, a new parameter takes its default (`StrategyParameter.DefaultValue`), and the value of a removed parameter is dropped.
  The same rule fits the run's applied values for the engine and the bot's own values (`Bot.FitParameters`) in the same root commit,
  so the bot's settings always name the parameters of the version it runs. This holds while the bot runs; it is the only change to a
  running bot's values, and it comes from the strategy, not from the owner.
- **Running bot (ADR 0019).** `Follow` fits the run's applied values to the latest version before checking and compiling it. A version
  is still rejected once when the fitted values do not fit it (a whole-number parameter with a fractional value), when it does not
  compile, or when the engine refuses it.
- **Start of a stored run (ADR 0025).** When the bot's values or allocation differ from the run's, the run takes them on the
  strategy's **latest** version, the one `ChangeBotParameters` checked them against, fitted to its parameters, in **one** root: the
  graph switches once, the capital is adjusted, the applied values are recorded and one `Resume(GraphChanged)` is drained. A refusal
  leaves the run as it was and rejects the latest version, not the one the run already runs. Without a settings change the start does
  nothing new and `Follow` switches on the first root as before.
- **Fresh start.** A start without a stored run (a first run, or an untraded run that is forgotten) fits the bot's values to the latest
  version before it checks and compiles them, so a parameter added while the bot was stopped does not block the start.
- **Visibility.** `GET /api/strategies/{id}/live-impact` returns each running bot's `parameters`. The editor's live-impact prompt
  compares them with the draft and names, per bot, the new parameters with the default each takes and the parameters dropped
  (for example `BTCUSDT 1h: buyAmount = 50 (по умолчанию), без rise`), before the owner confirms the save. A fit is recorded in the bot's
  feed as `SettingsChanged` with `setting=parameters`, `added` (`name=value` pairs) and `removed` (names), from `BotParametersFitted`.

## Consequences

- Saving a version that adds a parameter no longer stops the bots that follow it; they trade with the parameter's default until the
  owner changes it (stop, set the value, start: the run takes it on the next start by ADR 0025).
- The default is chosen in the editor. A default that is wrong for live money takes effect on every running bot of the strategy;
  the live-impact prompt shows it before the save, which is the owner's check.
- A version that changes a parameter's type to whole numbers is still rejected by a bot holding a fractional value; the owner sets a
  whole value while the bot is stopped.
- Backtests, replay and the engine are untouched.

## Tests

- Domain: `BotParameters.Fit` keeps known values, takes defaults and drops removed names; `Bot.FitParameters` changes a running bot's
  values and raises `BotParametersFitted` once, and raises nothing when the values already fit.
- Integration on the scripted venue: a running bot takes a version that adds a parameter with its default and a later version that
  removes it, without a rejection and with two `BotParametersFitted`; a stored run whose owner set new values and allocation while a
  version with a new parameter was saved starts on that version with the owner's values, without a rejection; a version whose
  whole-number parameter the run's value does not fit is rejected once; `live-impact` returns the bot's values.
- Frontend: the prompt names the default a bot takes, and the feed shows the fitted parameters.
