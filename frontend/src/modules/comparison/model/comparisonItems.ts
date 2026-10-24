/** A run, or a trial of an optimization identified by its window and index. */
export type ComparisonItem =
  | { readonly kind: 'run'; readonly id: string }
  | {
      readonly kind: 'trial';
      readonly optimizationId: string;
      readonly windowIndex: number;
      readonly trialIndex: number;
    };

/** At most this many columns fit side by side. */
export const COMPARISON_LIMIT = 6;

export function itemKey(item: ComparisonItem): string {
  return item.kind === 'run'
    ? `run:${item.id}`
    : `trial:${item.optimizationId}:${item.windowIndex}:${item.trialIndex}`;
}

/** The comparison's address: `runs` and `trials` as comma-separated lists, trials as `optimization:window:index`. */
export function comparisonSearch(items: readonly ComparisonItem[]): {
  runs?: string;
  trials?: string;
} {
  const runs = items.filter((item) => item.kind === 'run').map((item) => item.id);
  const trials = items
    .filter((item) => item.kind === 'trial')
    .map((item) => `${item.optimizationId}:${item.windowIndex}:${item.trialIndex}`);

  return {
    ...(runs.length > 0 ? { runs: runs.join(',') } : {}),
    ...(trials.length > 0 ? { trials: trials.join(',') } : {}),
  };
}

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Reads the address back; malformed entries are skipped and the list is cut at the limit. */
export function parseComparison(search: { runs?: string; trials?: string }): ComparisonItem[] {
  const runs: ComparisonItem[] = (search.runs ?? '')
    .split(',')
    .filter((id) => uuid.test(id))
    .map((id) => ({ kind: 'run', id }));
  const trials: ComparisonItem[] = (search.trials ?? '')
    .split(',')
    .flatMap((entry): ComparisonItem[] => {
      const [optimizationId = '', window = '', index = ''] = entry.split(':');

      return uuid.test(optimizationId) && /^\d{1,7}$/.test(window) && /^\d{1,7}$/.test(index)
        ? [
            {
              kind: 'trial',
              optimizationId,
              windowIndex: Number(window),
              trialIndex: Number(index),
            },
          ]
        : [];
    });

  return [...runs, ...trials].slice(0, COMPARISON_LIMIT);
}
