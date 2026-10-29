import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';

import { backtestKeys } from '@/modules/backtests';
import { activeJobsFingerprint, ActiveJobsPanel, activeJobsQuery } from '@/modules/jobs';
import { optimizationKeys } from '@/modules/optimizations';
import { Card } from '@/shared/ui/Card';
import { PageHeader } from '@/shared/ui/PageHeader';

import { OptimizationsList } from './OptimizationsList';
import { RunsList } from './RunsList';
import { backtestsRoute, type ListSearch } from './search';

const tabs = [
  { id: 'runs', label: 'Прогоны' },
  { id: 'optimizations', label: 'Оптимизации' },
] as const;

/**
 * The user's background work and its results: what runs and waits in the Worker's queues, then finished runs and
 * optimizations with filters kept in the address. A job that starts, pauses or ends refreshes the lists.
 */
export function BacktestsPage() {
  const search = backtestsRoute.useSearch();
  const navigate = backtestsRoute.useNavigate();
  const queryClient = useQueryClient();
  const active = useQuery(activeJobsQuery());
  const fingerprint = active.data ? activeJobsFingerprint(active.data.jobs) : undefined;
  const seen = useRef<string | undefined>(undefined);
  const tab = search.tab ?? 'runs';

  useEffect(() => {
    if (fingerprint === undefined) {
      return;
    }

    if (seen.current !== undefined && seen.current !== fingerprint) {
      void queryClient.invalidateQueries({ queryKey: backtestKeys.rankings });
      void queryClient.invalidateQueries({ queryKey: backtestKeys.facets });
      void queryClient.invalidateQueries({ queryKey: optimizationKeys.lists });
    }

    seen.current = fingerprint;
  }, [fingerprint, queryClient]);

  function update(changes: Partial<ListSearch>) {
    void navigate({
      search: (previous) =>
        Object.fromEntries(
          Object.entries({ ...previous, ...changes }).filter(([, value]) => value !== undefined),
        ),
      replace: true,
    });
  }

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <PageHeader
        title="Прогоны"
        meta="Что сейчас выполняется и ждёт в очереди, завершённые прогоны и оптимизации. Фильтры сохраняются в адресе страницы."
      />
      <ActiveJobsPanel />
      <Card>
        <div role="tablist" aria-label="Списки" className="mb-4 flex gap-4 border-b border-line">
          {tabs.map((item) => (
            <button
              key={item.id}
              id={`tab-${item.id}`}
              type="button"
              role="tab"
              aria-selected={tab === item.id}
              aria-controls={`panel-${item.id}`}
              onClick={() => update({ tab: item.id === 'runs' ? undefined : item.id })}
              className={`-mb-px border-b-2 px-1 pb-2 text-sm ${
                tab === item.id
                  ? 'border-text text-text'
                  : 'border-transparent text-text-secondary hover:text-text'
              }`}
            >
              {item.label}
            </button>
          ))}
        </div>
        <div role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`}>
          {tab === 'runs' ? (
            <RunsList search={search} update={update} />
          ) : (
            <OptimizationsList search={search} update={update} />
          )}
        </div>
      </Card>
    </div>
  );
}
