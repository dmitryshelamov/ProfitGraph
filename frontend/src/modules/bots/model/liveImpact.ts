import type { LiveImpactBot } from './types';

/** A parameter of the version being saved, as the editor's draft names it. */
export interface DraftParameter {
  readonly name: string;
  readonly defaultValue: string;
}

/** How a running bot's values follow the saved version (ADR 0047). */
export interface ParameterFit {
  /** New parameters with the default each bot takes. */
  readonly added: readonly DraftParameter[];
  /** Names whose values the bot drops. */
  readonly removed: readonly string[];
}

/** A new parameter takes its default and a removed parameter's value is dropped, as the bot does when it switches. */
export function parameterFit(
  bot: LiveImpactBot,
  parameters: readonly DraftParameter[],
): ParameterFit {
  const names = new Set(parameters.map((parameter) => parameter.name));

  return {
    added: parameters.filter((parameter) => !(parameter.name in bot.parameters)),
    removed: Object.keys(bot.parameters)
      .filter((name) => !names.has(name))
      .sort(),
  };
}

/** One line per bot whose values change, or null when no bot's values change. */
export function parameterFitText(
  bots: readonly LiveImpactBot[],
  parameters: readonly DraftParameter[],
): string | null {
  const lines = bots.flatMap((bot) => {
    const { added, removed } = parameterFit(bot, parameters);
    const parts = [
      ...added.map((parameter) => `${parameter.name} = ${parameter.defaultValue} (по умолчанию)`),
      ...removed.map((name) => `без ${name}`),
    ];

    return parts.length > 0 ? [`${bot.symbol} ${bot.timeframe}: ${parts.join(', ')}`] : [];
  });

  return lines.length > 0 ? lines.join('; ') : null;
}
