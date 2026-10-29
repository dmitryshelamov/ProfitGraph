import {
  compareDecimals,
  divideDecimals,
  multiplyDecimals,
  subtractDecimals,
} from '@/shared/lib/decimal';

import type { ScanTradeIdea } from './types';

const places = 8;

/** What a trade card's idea makes at its target and loses at its stop, after the fees its sizing assumed (ADR 0060). */
export interface IdeaOutcome {
  /** The quote made at the target after fees; null until the idea is sized. */
  readonly profit: string | null;
  /** The profit in percent of the trade's amount. */
  readonly profitPercent: string | null;
  /** The quote lost at the stop after fees, as a positive number; null until the idea is sized. */
  readonly loss: string | null;
  /** The loss in percent of the trade's amount, as a positive number. */
  readonly lossPercent: string | null;
  /** The profit over the loss: the reward-to-risk after fees. */
  readonly netRewardRisk: string | null;
  /** How far the price moves from the entry to the target, in percent, signed. */
  readonly targetMove: string | null;
  /** How far the price moves from the entry to the stop, in percent, signed. */
  readonly stopMove: string | null;
}

/**
 * The idea's outcome from its amount and risk. The backend sizes a trade so that the stop loses the risk with a fee on both sides
 * (`risk = amount × (|entry − stop| / entry + 2 × fee)`), so the profit at the target after the same fees is
 * `amount × |target − stop| / entry − risk`, with no fee rate needed here.
 */
export function ideaOutcome(idea: ScanTradeIdea): IdeaOutcome {
  const targetMove = move(idea.entry, idea.target);
  const stopMove = move(idea.entry, idea.stop);
  const span = distance(idea.target, idea.stop);
  const reach = span && idea.amount ? multiplyDecimals(idea.amount, span) : null;
  const gross = reach ? divideDecimals(reach, idea.entry, places) : null;
  const profit = gross && idea.risk ? subtractDecimals(gross, idea.risk) : null;

  return {
    profit,
    profitPercent: percentOf(profit, idea.amount),
    loss: idea.risk,
    lossPercent: percentOf(idea.risk, idea.amount),
    netRewardRisk: profit && idea.risk ? signedDivide(profit, idea.risk) : null,
    targetMove,
    stopMove,
  };
}

function move(from: string, to: string): string | null {
  const change = subtractDecimals(to, from);

  return change ? signedDivide(multiplyDecimals(change, '100') ?? change, from) : null;
}

function percentOf(value: string | null, base: string | null): string | null {
  return value && base ? signedDivide(multiplyDecimals(value, '100') ?? value, base) : null;
}

function distance(left: string, right: string): string | null {
  const order = compareDecimals(left, right);

  return order === null
    ? null
    : order >= 0
      ? subtractDecimals(left, right)
      : subtractDecimals(right, left);
}

function signedDivide(value: string, by: string): string | null {
  if ((compareDecimals(value, '0') ?? 0) >= 0) {
    return divideDecimals(value, by, places);
  }

  const magnitude = divideDecimals(subtractDecimals('0', value) ?? value, by, places);

  return magnitude ? subtractDecimals('0', magnitude) : null;
}
