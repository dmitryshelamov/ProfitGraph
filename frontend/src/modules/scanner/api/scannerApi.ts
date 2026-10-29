import { infiniteQueryOptions, queryOptions } from '@tanstack/react-query';

import { pageBefore } from '@/modules/market-chart';
import { api, unwrap } from '@/shared/api/client';

import type {
  ScanBacktest,
  ScanCandlesPage,
  ScanLevels,
  ScanLiveTrade,
  ScanPreset,
  ScanPresetInput,
  ScanTradeIdea,
  ScanTradeSettings,
  ScanTradeSettingsInput,
} from '../model/types';

export const scannerKeys = {
  venues: ['scanner', 'venues'] as const,
  snapshot: (venue: string) => ['scanner', venue, 'snapshot'] as const,
  presets: (venue: string) => ['scanner', venue, 'presets'] as const,
  candles: (venue: string, symbol: string, timeframe: string) =>
    ['scanner', venue, 'candles', symbol, timeframe] as const,
  newCandles: (venue: string, symbol: string, timeframe: string, from: string) =>
    ['scanner', venue, 'new-candles', symbol, timeframe, from] as const,
  levels: (venue: string, symbol: string, timeframe: string) =>
    ['scanner', venue, 'levels', symbol, timeframe] as const,
  backtests: (venue: string) => ['scanner', venue, 'backtests'] as const,
  backtest: (id: string) => ['scanner', 'backtests', id] as const,
  tradeIdeas: (venue: string, symbol: string) => ['scanner', venue, 'trade-ideas', symbol] as const,
  tradeSettings: (venue: string) => ['scanner', venue, 'trade-settings'] as const,
  trades: (venue: string) => ['scanner', venue, 'trades'] as const,
  pairTrades: (venue: string, symbol: string) => ['scanner', venue, 'trades', symbol] as const,
};

export function scanVenuesQuery() {
  return queryOptions({
    queryKey: scannerKeys.venues,
    queryFn: async () => unwrap(await api.GET('/api/scanner/venues')),
  });
}

/** The venue's shared snapshot; the page reads it again whenever the scanner announces a new one. */
export function scanSnapshotQuery(venue: string) {
  return queryOptions({
    queryKey: scannerKeys.snapshot(venue),
    queryFn: async () =>
      unwrap(await api.GET('/api/scanner/{venue}/snapshot', { params: { path: { venue } } })),
  });
}

/** The venue's built-in presets, then the user's own. */
export function scanPresetsQuery(venue: string) {
  return queryOptions({
    queryKey: scannerKeys.presets(venue),
    queryFn: async () =>
      unwrap(await api.GET('/api/scanner/{venue}/presets', { params: { path: { venue } } })),
  });
}

export async function createScanPreset(venue: string, input: ScanPresetInput): Promise<ScanPreset> {
  return unwrap(
    await api.POST('/api/scanner/{venue}/presets', { params: { path: { venue } }, body: input }),
  );
}

export async function updateScanPreset(
  presetId: string,
  input: ScanPresetInput,
): Promise<ScanPreset> {
  return unwrap(
    await api.PUT('/api/scanner/presets/{presetId}', {
      params: { path: { presetId } },
      body: input,
    }),
  );
}

/** Switches the Telegram alerts of the user's own preset on or off. */
export async function setScanPresetAlerts(presetId: string, enabled: boolean): Promise<ScanPreset> {
  return unwrap(
    await api.PUT('/api/scanner/presets/{presetId}/alerts', {
      params: { path: { presetId } },
      body: { enabled },
    }),
  );
}

export async function deleteScanPreset(presetId: string): Promise<void> {
  const result = await api.DELETE('/api/scanner/presets/{presetId}', {
    params: { path: { presetId } },
  });

  if (!result.response.ok) {
    unwrap(result);
  }
}

async function scanCandles(
  venue: string,
  symbol: string,
  timeframe: string,
  from: string,
  to: string,
): Promise<ScanCandlesPage> {
  return unwrap(
    await api.GET('/api/scanner/{venue}/candles', {
      params: { path: { venue }, query: { symbol, timeframe, from, to } },
    }),
  );
}

/**
 * A pair's closed candles, a page of `chartPageCandles` candles at a time going back from now; the server cuts each page at
 * the latest closed candle. Paging stops at a page without candles: the venue's history begins there or cannot be read.
 */
export function scanCandlesQuery(venue: string, symbol: string, timeframe: string) {
  return infiniteQueryOptions({
    queryKey: scannerKeys.candles(venue, symbol, timeframe),
    queryFn: async ({ pageParam }) => {
      const window = pageBefore(pageParam ?? new Date().toISOString(), timeframe);

      return scanCandles(venue, symbol, timeframe, window.from, window.to);
    },
    initialPageParam: null as string | null,
    getNextPageParam: (page) => (page.candles.length === 0 ? undefined : page.from),
    staleTime: Number.POSITIVE_INFINITY,
    refetchOnWindowFocus: false,
  });
}

/** Candles closed since `from`, the end of the newest loaded page, read again every minute. */
export function scanNewCandlesQuery(
  venue: string,
  symbol: string,
  timeframe: string,
  from: string,
) {
  return queryOptions({
    queryKey: scannerKeys.newCandles(venue, symbol, timeframe, from),
    queryFn: async () => scanCandles(venue, symbol, timeframe, from, new Date().toISOString()),
    refetchInterval: 60_000,
  });
}

/**
 * A pair's support, resistance and reward-to-risk on the chart's timeframe, from its last closed candles of that timeframe
 * at the price of its snapshot row; read again every minute as candles close and the price moves.
 */
export function scanLevelsQuery(venue: string, symbol: string, timeframe: string) {
  return queryOptions({
    queryKey: scannerKeys.levels(venue, symbol, timeframe),
    queryFn: async (): Promise<ScanLevels> =>
      unwrap(
        await api.GET('/api/scanner/{venue}/levels', {
          params: { path: { venue }, query: { symbol, timeframe } },
        }),
      ),
    refetchInterval: 60_000,
  });
}

/** Replays the preset's conditions over the venue's last 30 days in a background job. */
export async function startScanBacktest(
  venue: string,
  input: ScanPresetInput,
): Promise<ScanBacktest> {
  return unwrap(
    await api.POST('/api/scanner/{venue}/backtests', { params: { path: { venue } }, body: input }),
  );
}

/** The user's ten latest backtests on the venue, newest first. */
export function scanBacktestsQuery(venue: string) {
  return queryOptions({
    queryKey: scannerKeys.backtests(venue),
    queryFn: async () =>
      unwrap(await api.GET('/api/scanner/{venue}/backtests', { params: { path: { venue } } })),
  });
}

/** A backtest, read again every few seconds until its result is stored. */
export function scanBacktestQuery(backtestId: string) {
  return queryOptions({
    queryKey: scannerKeys.backtest(backtestId),
    queryFn: async () =>
      unwrap(
        await api.GET('/api/scanner/backtests/{backtestId}', {
          params: { path: { backtestId } },
        }),
      ),
    refetchInterval: (query) => (query.state.data?.completedAt ? false : 3000),
  });
}

/** The pair's trade cards on the levels of every timeframe (ADR 0060), read again every minute. */
export function scanTradeIdeasQuery(venue: string, symbol: string) {
  return queryOptions({
    queryKey: scannerKeys.tradeIdeas(venue, symbol),
    queryFn: async () =>
      unwrap(
        await api.GET('/api/scanner/{venue}/trade-ideas', {
          params: { path: { venue }, query: { symbol } },
        }),
      ),
    refetchInterval: 60_000,
  });
}

/** The owner's trade card settings on the venue; null before they are set. */
export function scanTradeSettingsQuery(venue: string) {
  return queryOptions({
    queryKey: scannerKeys.tradeSettings(venue),
    queryFn: async () =>
      unwrap(await api.GET('/api/scanner/{venue}/trade-settings', { params: { path: { venue } } }))
        .settings ?? null,
  });
}

export async function setScanTradeSettings(
  venue: string,
  input: ScanTradeSettingsInput,
): Promise<ScanTradeSettings> {
  return unwrap(
    await api.PUT('/api/scanner/{venue}/trade-settings', {
      params: { path: { venue } },
      body: input,
    }),
  );
}

/** The owner's latest trades from cards on the pair, read again every 15 seconds while one is live. */
export function scanPairTradesQuery(venue: string, symbol: string) {
  return queryOptions({
    queryKey: scannerKeys.pairTrades(venue, symbol),
    queryFn: async () =>
      unwrap(
        await api.GET('/api/scanner/{venue}/trades', {
          params: { path: { venue }, query: { symbol } },
        }),
      ),
    refetchInterval: (query) =>
      query.state.data?.some((trade) => trade.status === 'Waiting' || trade.status === 'Open')
        ? 15_000
        : false,
  });
}

/** Creates a trade from a card with real orders: its own bot, started at once. */
export async function createScanTrade(
  venue: string,
  symbol: string,
  idea: ScanTradeIdea,
): Promise<ScanLiveTrade> {
  return unwrap(
    await api.POST('/api/scanner/{venue}/trades', {
      params: { path: { venue } },
      body: {
        symbol,
        timeframe: idea.timeframe,
        scenario: idea.scenario,
        entry: idea.entry,
        stop: idea.stop,
        target: idea.target,
        direction: idea.direction,
      },
    }),
  );
}
