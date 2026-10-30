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

/** What each short card proposes (ADR 0061): it sells at the entry. */
export const shortScenarioHints: Record<string, string> = {
  Levels: 'Продажа по текущей цене, стоп над сопротивлением, цель у поддержки.',
  Target2R: 'Продажа по текущей цене, стоп над сопротивлением, цель — два риска ниже.',
  Pullback:
    'Лимитка на середине последнего часа падения, стоп на максимуме часа, цель — два риска ниже.',
};

/** The hint of a card in its direction. */
export function tradeScenarioHint(
  scenario: string,
  direction: string | undefined,
): string | undefined {
  return (direction === 'Short' ? shortScenarioHints : tradeScenarioHints)[scenario];
}

/** The label of a trade's direction. */
export function tradeDirectionLabel(direction: string | undefined): string {
  return direction === 'Short' ? 'Шорт' : 'Лонг';
}

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
  scan_trade_entry_beyond_levels: 'цена ушла за стоп или цель до входа',
  scan_trade_entry_price_unknown: 'нет свежей цены перед входом',
};

export function tradeFailure(code: string | null | undefined): string | null {
  return code ? (failures[code] ?? code) : null;
}

export function isLiveTrade(trade: ScanLiveTrade): boolean {
  return trade.status === 'Waiting' || trade.status === 'Open';
}

/** Why the settings cannot be saved yet, or null. A leverage is checked only where the venue has one (ADR 0061). */
export function settingsProblem(
  credentialId: string,
  risk: string,
  maxAmount: string,
  leverage: string | null = null,
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

  if (leverage !== null) {
    const value = leverage.trim();

    if (
      !isBackendDecimal(value) ||
      (compareDecimals(value, '1') ?? -1) < 0 ||
      (compareDecimals(value, '100') ?? 1) > 0 ||
      (value.split('.')[1]?.length ?? 0) > 2
    ) {
      return 'Плечо — число от 1 до 100, не больше двух знаков после точки.';
    }
  }

  return null;
}
