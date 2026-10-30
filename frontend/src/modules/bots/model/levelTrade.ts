import type { ChartLevel } from '@/modules/market-chart';
import {
  compareDecimals,
  divideDecimals,
  isDecimalString,
  multiplyDecimals,
  subtractDecimals,
} from '@/shared/lib/decimal';

import { atPlaces } from './botDisplay';
import type { Bot } from './types';

/**
 * One trade between given prices, as the level trade strategy runs it (ADR 0060, short ADR 0061) for a market scanner's
 * trade card: a limit entry, a limit exit at the target and a market exit at the stop. The amount is the quote the entry
 * spends, or for a short the quote it sells for.
 */
export interface LevelTrade {
  readonly direction: 'Long' | 'Short';
  readonly amount: string;
  readonly entry: string;
  readonly stop: string;
  readonly target: string;
}

/** Decimal places the planned outcome is computed with before it is rounded for display. */
const outcomePlaces = 8;

function positive(value: string | undefined): value is string {
  return value !== undefined && isDecimalString(value) && compareDecimals(value, '0') === 1;
}

/**
 * The trade a bot's parameters describe: `entryPrice`, `stopPrice`, `targetPrice` with `buyAmount` for a long or `sellAmount`
 * for a short. Null for a bot of another strategy or with a missing or non-positive value.
 */
export function levelTrade(bot: Pick<Bot, 'parameters'>): LevelTrade | null {
  const { entryPrice, stopPrice, targetPrice, buyAmount, sellAmount } = bot.parameters;

  if (!positive(entryPrice) || !positive(stopPrice) || !positive(targetPrice)) {
    return null;
  }

  const short = buyAmount === undefined && sellAmount !== undefined;
  const amount = short ? sellAmount : buyAmount;

  if (!positive(amount)) {
    return null;
  }

  return {
    direction: short ? 'Short' : 'Long',
    amount,
    entry: entryPrice,
    stop: stopPrice,
    target: targetPrice,
  };
}

/** The magnitude of `left − right`. */
function distance(left: string, right: string): string | null {
  const difference = subtractDecimals(left, right);

  if (difference === null) {
    return null;
  }

  return difference.startsWith('-') ? difference.slice(1) : difference;
}

/**
 * How far `price` is from `reference` in percent of the reference, rounded down to hundredths and signed: "+4.76%",
 * "−9.52%", "0%". Null for a value that is not a decimal or a non-positive reference.
 */
export function percentFrom(price: string, reference: string): string | null {
  const direction = compareDecimals(price, reference);
  const magnitude = distance(price, reference);

  if (direction === null || magnitude === null || compareDecimals(reference, '0') !== 1) {
    return null;
  }

  const percent = divideDecimals(multiplyDecimals(magnitude, '100') ?? '', reference, 2);

  if (percent === null) {
    return null;
  }

  return direction > 0 ? `+${percent}%` : direction < 0 ? `−${percent}%` : `${percent}%`;
}

/**
 * What the trade loses at the stop and makes at the target if the whole amount enters at the entry price, in quote and
 * before fees and slippage: the quantity `amount / entry` times the distance to each exit. The stop is a market order, so
 * the real loss may be larger.
 */
export function plannedOutcome(trade: LevelTrade): { loss: string; profit: string } | null {
  const lossDistance = distance(trade.entry, trade.stop);
  const profitDistance = distance(trade.target, trade.entry);
  const loss = divideDecimals(
    multiplyDecimals(trade.amount, lossDistance ?? '') ?? '',
    trade.entry,
    outcomePlaces,
  );
  const profit = divideDecimals(
    multiplyDecimals(trade.amount, profitDistance ?? '') ?? '',
    trade.entry,
    outcomePlaces,
  );

  return loss === null || profit === null ? null : { loss, profit };
}

/** Human words for when the stop fires: on the close of the bot's own timeframe's candle, at or past the stop. */
export function stopRule(trade: LevelTrade, timeframe: string): string {
  const past = trade.direction === 'Long' ? 'на уровне стопа или ниже' : 'на уровне стопа или выше';

  return `${trade.direction === 'Long' ? 'Продажа' : 'Покупка'} по рынку, когда свеча ${timeframe} закрылась ${past}`;
}

/**
 * The trade's entry, target and stop as solid chart lines: the entry in the neutral color, the target green and the stop red.
 * A working order at the same price is folded into its line, its title after the level's, so the two are not drawn over each
 * other; the other orders stay dashed.
 */
export function levelTradeLevels(
  trade: LevelTrade,
  timeframe: string,
  orders: readonly ChartLevel[],
  pricePlaces: number | null,
): ChartLevel[] {
  const rest = [...orders];
  const withOrder = (level: ChartLevel): ChartLevel => {
    const index = rest.findIndex((order) => compareDecimals(order.price, level.price) === 0);

    if (index < 0) {
      return level;
    }

    const [order] = rest.splice(index, 1);

    return { ...level, title: `${level.title} · ${order!.title}` };
  };
  const price = (value: string) => atPlaces(value, pricePlaces);
  const levels = [
    withOrder({
      id: 'level-trade:entry',
      side: trade.direction === 'Long' ? 'Buy' : 'Sell',
      price: trade.entry,
      title: `Вход ${price(trade.entry)}`,
      solid: true,
      neutral: true,
    }),
    withOrder({
      id: 'level-trade:target',
      side: 'Buy',
      price: trade.target,
      title: `Цель ${price(trade.target)}`,
      solid: true,
    }),
    {
      id: 'level-trade:stop',
      side: 'Sell',
      price: trade.stop,
      title: `Стоп ${price(trade.stop)} · по закрытию ${timeframe}`,
      solid: true,
    } satisfies ChartLevel,
  ];

  return [...levels, ...rest];
}
