import { addDecimals, divideDecimals, multiplyDecimals } from '@/shared/lib/decimal';

import type { ScanAutoTradeThird } from './types';

/** What came of a rule's signal (ADR 0071, section 6). */
export const autoTradeRequestLabels: Record<string, string> = {
  Pending: 'Ждёт',
  Handling: 'Обрабатывается',
  Paper: 'На бумаге',
  Created: 'Сделка',
  Expired: 'Устарел',
  Paused: 'Правило выключено или на паузе',
  LimitOpen: 'Лимит открытых сделок',
  LimitDay: 'Лимит сделок за день',
  PairBusy: 'Пара уже в сделке',
  NoIdea: 'Нет подходящей карточки',
  Refused: 'Отказ',
};

/** Where a paper trade stands (ADR 0071, section 7). */
export const paperTradeLabels: Record<string, string> = {
  Waiting: 'Ждёт входа',
  Open: 'Открыта',
  Unfilled: 'Вход не исполнился',
  Target: 'Цель',
  Stop: 'Стоп',
  Time: 'По времени',
};

/** A rule's modes. */
export const autoTradeModeLabels: Record<string, string> = {
  Off: 'Выключено',
  Paper: 'На бумаге',
  Live: 'Реальные сделки',
};

/** The trades of all thirds and their mean R weighted by the thirds' trades; null without a trade. */
export function overall(thirds: readonly ScanAutoTradeThird[]): {
  trades: number;
  meanR: string | null;
} {
  const trades = thirds.reduce((sum, third) => sum + third.trades, 0);

  if (trades === 0) {
    return { trades, meanR: null };
  }

  const total = thirds.reduce(
    (sum, third) =>
      third.meanR === null
        ? sum
        : (addDecimals(sum, multiplyDecimals(third.meanR, String(third.trades)) ?? '0') ?? sum),
    '0',
  );

  return { trades, meanR: divideDecimals(total, String(trades), 4) };
}
