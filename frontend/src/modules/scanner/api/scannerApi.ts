import { queryOptions } from '@tanstack/react-query';

import { api, unwrap } from '@/shared/api/client';

import type { ScanPreset, ScanPresetInput } from '../model/types';

export const scannerKeys = {
  venues: ['scanner', 'venues'] as const,
  snapshot: (venue: string) => ['scanner', venue, 'snapshot'] as const,
  presets: (venue: string) => ['scanner', venue, 'presets'] as const,
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
