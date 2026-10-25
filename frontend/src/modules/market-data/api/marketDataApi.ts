import { queryOptions } from '@tanstack/react-query';

import { api, type Schemas, unwrap } from '@/shared/api/client';

import { venuesReadWithKey } from '../model/seriesSelection';

export const marketDataKeys = {
  venues: ['market-data', 'venues'] as const,
  instruments: (venue: string, query: string, credentialId?: string) =>
    ['market-data', 'instruments', venue, query, credentialId ?? ''] as const,
};

export function venuesQuery() {
  return queryOptions({
    queryKey: marketDataKeys.venues,
    queryFn: async () => unwrap(await api.GET('/api/market-data/venues')),
    staleTime: Infinity,
  });
}

/**
 * Pairs of a venue whose symbol or assets contain the query; the venue's list changes rarely. A venue read with the user's
 * key searches only once a key is chosen.
 */
export function instrumentsQuery(venue: string, query: string, credentialId?: string) {
  return queryOptions({
    queryKey: marketDataKeys.instruments(venue, query, credentialId),
    queryFn: async () =>
      unwrap(
        await api.GET('/api/market-data/instruments', {
          params: { query: { venue, query, limit: 20, ...(credentialId ? { credentialId } : {}) } },
        }),
      ),
    enabled: venue !== '' && (!venuesReadWithKey.has(venue) || credentialId !== undefined),
    staleTime: 5 * 60 * 1000,
  });
}

export type HistoryDownloadInput = Schemas['DownloadCandlesRequest'];

/** Queues a download of the missing public history; the job reports progress. */
export async function startHistoryDownload(
  input: HistoryDownloadInput,
): Promise<Schemas['BackgroundJobResponse']> {
  return unwrap(await api.POST('/api/historical-data/downloads', { body: input }));
}

export interface CsvImportInput {
  readonly venue: string;
  readonly symbol: string;
  readonly timeframe: string;
  readonly file: File;
}

/** Imports candles from a CSV file into the shared cache; only the Owner may. */
export async function importCandlesCsv(
  input: CsvImportInput,
): Promise<Schemas['CandleImportResponse']> {
  const form = new FormData();
  form.set('Venue', input.venue);
  form.set('Symbol', input.symbol);
  form.set('Timeframe', input.timeframe);
  form.set('File', input.file);

  return unwrap(
    await api.POST('/api/historical-data/csv', {
      // The generated type describes the form fields; openapi-fetch sends FormData as is, with the browser's boundary.
      body: form as never,
    }),
  );
}
