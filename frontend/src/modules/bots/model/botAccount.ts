import type { BotAccount } from './types';

const accountIdLabels: Readonly<Record<string, string>> = {
  bybit: 'UID',
  tinvest: 'Счёт',
  steam: 'SteamID',
};

const kindLabels: Readonly<Record<string, string>> = {
  main: 'основной аккаунт',
  sub: 'субаккаунт',
  broker: 'брокерский счёт',
  iis: 'ИИС',
  other: 'другой счёт',
};

const accessLabels: Readonly<Record<string, string>> = {
  read_write: 'чтение и торговля',
  read_only: 'только чтение',
  full_access: 'полный доступ',
  no_access: 'нет доступа',
};

/** How the account is named on its venue: Bybit's UID, a T-Invest account number or a SteamID, and its identifier. */
export function accountIdentifier(
  account: Pick<BotAccount, 'venue' | 'identity' | 'accountPublicId'>,
): { label: string; value: string } | null {
  const value = account.identity?.accountId ?? account.accountPublicId;

  return value ? { label: accountIdLabels[account.venue] ?? 'Аккаунт', value } : null;
}

export function accountKindLabel(kind: string): string {
  return kindLabels[kind] ?? kind;
}

export function accessLevelLabel(level: string): string {
  return accessLabels[level] ?? level;
}

/** How long ago a UTC instant was, for display: seconds, minutes, hours, then days. */
export function ageLabel(at: string, now: number): string {
  const seconds = Math.max(0, Math.round((now - Date.parse(at)) / 1000));

  if (seconds < 60) {
    return `${seconds} с назад`;
  }

  if (seconds < 3600) {
    return `${Math.round(seconds / 60)} мин назад`;
  }

  if (seconds < 86_400) {
    return `${Math.round(seconds / 3600)} ч назад`;
  }

  return `${Math.round(seconds / 86_400)} дн назад`;
}

/** A snapshot older than fifteen minutes is stale: the Worker reads a key's account every five minutes while it works. */
export function isStale(at: string, now: number): boolean {
  return now - Date.parse(at) > 15 * 60_000;
}
