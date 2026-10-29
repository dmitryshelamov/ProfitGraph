import { infiniteQueryOptions, queryOptions } from '@tanstack/react-query';

import { pageBefore } from '@/modules/market-chart';
import { api, unwrap } from '@/shared/api/client';

import type { ScanCandlesPage, ScanPreset, ScanPresetInput } from '../model/types';

export const scannerKeys = {
  venues: ['scanner', 'venues'] as const,
  snapshot: (venue: string) => ['scanner', venue, 'snapshot'] as const,
  presets: (venue: string) => ['scanner', venue, 'presets'] as const,
  candles: (venue: string, symbol: string, timeframe: string) =>
    ['scanner', venue, 'candles', symbol, timeframe] as const,
  newCandles: (venue: string, symbol: string, timeframe: string, from: string) =>
    ['scanner', venue, 'new-candles', symbol, timeframe, from] as const,
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
