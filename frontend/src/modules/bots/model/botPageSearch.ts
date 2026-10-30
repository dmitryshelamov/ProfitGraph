import { botChartTimeframe } from './chartTimeframes';

/** The tabs under the bot's chart, the first one open by default. */
export const botPageTabs = [
  { id: 'orders', label: 'Ордера' },
  { id: 'fills', label: 'Сделки' },
  { id: 'feed', label: 'Лента' },
  { id: 'alerts', label: 'Алерты' },
  { id: 'settings', label: 'Настройки' },
] as const;

export type BotPageTab = (typeof botPageTabs)[number]['id'];

/** The bot page's chart timeframe and open tab as the address holds them; the defaults stay out of the address. */
export interface BotPageSearch {
  readonly timeframe?: string;
  readonly tab?: Exclude<BotPageTab, 'orders'>;
}

function isTab(value: unknown): value is Exclude<BotPageTab, 'orders'> {
  return botPageTabs.some((tab) => tab.id !== 'orders' && tab.id === value);
}

/** Keeps a known timeframe and a tab other than the default one; anything else is dropped. */
export function botPageSearch(search: Record<string, unknown>): BotPageSearch {
  const timeframe = botChartTimeframe(search.timeframe);

  return {
    ...(timeframe ? { timeframe } : {}),
    ...(isTab(search.tab) ? { tab: search.tab } : {}),
  };
}
