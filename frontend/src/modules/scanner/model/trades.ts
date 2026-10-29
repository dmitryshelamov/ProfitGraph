import { compareDecimals, isBackendDecimal } from '@/shared/lib/decimal';
import type { StatusTone } from '@/shared/ui/StatusBadge';

import type { ScanLiveTrade } from './types';

/** Short names of the trade cards' scenarios (ADR 0060). */
export const tradeScenarioLabels: Record<string, string> = {
  Levels: 'Уровни',
  LevelsRewardRisk2: 'Уровни',
  Target2R: 'Цель 2R',
  Pullback: 'Откат',
};

/** What each card proposes, under its name. */
export const tradeScenarioHints: Record<string, string> = {
  Levels: 'Вход по текущей цене, стоп на поддержке, цель у сопротивления.',
  Target2R: 'Вход по текущей цене, стоп на поддержке, цель — два риска выше.',
  Pullback:
    'Лимитка на середине последнего часа роста, стоп на минимуме часа, цель — два риска выше.',
};

const statuses: Record<ScanLiveTrade['status'], { label: string; tone: StatusTone }> = {
  Waiting: { label: 'Ждёт входа', tone: 'neutral' },
  Open: { label: 'В позиции', tone: 'success' },
  TookProfit: { label: 'Цель', tone: 'success' },
  StoppedOut: { label: 'Стоп', tone: 'danger' },
  Closed: { label: 'Закрыта', tone: 'neutral' },
  Expired: { label: 'Вход истёк', tone: 'neutral' },
  Cancelled: { label: 'Отменена', tone: 'neutral' },
  Failed: { label: 'Ошибка', tone: 'danger' },
};

export function tradeStatus(status: string): { label: string; tone: StatusTone } {
  return statuses[status] ?? { label: status, tone: 'neutral' };
}

const failures: Record<string, string> = {
  scan_trade_start_failed: 'бот не запустился',
  scan_trade_entry_ended: 'биржа не приняла вход',
};

export function tradeFailure(code: string | null | undefined): string | null {
  return code ? (failures[code] ?? code) : null;
}

export function isLiveTrade(trade: ScanLiveTrade): boolean {
  return trade.status === 'Waiting' || trade.status === 'Open';
}

/** Why the settings cannot be saved yet, or null. */
export function settingsProblem(
  credentialId: string,
  risk: string,
  maxAmount: string,
): string | null {
  if (!credentialId) {
    return 'Добавьте ключ этой биржи в разделе «Ключи».';
  }

  for (const [value, name] of [
    [risk, 'Риск'],
    [maxAmount, 'Максимум'],
  ] as const) {
    if (!isBackendDecimal(value.trim()) || (compareDecimals(value.trim(), '0') ?? 0) <= 0) {
      return `${name} — положительное число, например 5 или 2.5.`;
    }
  }

  return null;
}
