import { useQuery } from '@tanstack/react-query';
import { getRouteApi, Link } from '@tanstack/react-router';
import { useMemo, useState } from 'react';

import {
  defaultScanChartTimeframe,
  ideaLevels,
  scanChartTimeframes,
  scanLevelsQuery,
  ScanPairChart,
  ScanPairSummary,
  ScanPairTrades,
  ScanRowMetrics,
  scanSnapshotQuery,
  ScanTimeframeLevels,
  type ScanTradeIdea,
  scanTradeIdeasQuery,
  ScanTradeIdeasTable,
  ScanTradeSettingsForm,
  scanVenuesQuery,
  timeframeLevels,
  useScanSnapshotUpdates,
} from '@/modules/scanner';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Card } from '@/shared/ui/Card';
import { DateTime } from '@/shared/ui/DateTime';
import { PageHeader } from '@/shared/ui/PageHeader';

const route = getRouteApi('/_authenticated/_shell/scanner/$venue/$symbol');

/**
 * A scanner pair (ADR 0059, ADR 0060): its headline metrics, its chart on a chosen timeframe with that timeframe's support and
 * resistance, and the trade ideas of every timeframe as one table with what each makes and loses after fees. The trade settings and
 * every metric sit beside the chart; an idea hovered or ticked in the table is drawn on the chart.
 */
export function ScanPairPage() {
  const { venue, symbol } = route.useParams();
  const { timeframe = defaultScanChartTimeframe } = route.useSearch();
  const venues = useQuery(scanVenuesQuery());
  const snapshot = useQuery(scanSnapshotQuery(venue));
  const row = snapshot.data?.rows.find((item) => item.symbol === symbol);
  const levels = useQuery(scanLevelsQuery(venue, symbol, timeframe));
  const ideas = useQuery(scanTradeIdeasQuery(venue, symbol));
  const [preview, setPreview] = useState<readonly ScanTradeIdea[]>([]);
  const chartLevels = useMemo(
    () => [...timeframeLevels(levels.data), ...preview.flatMap(ideaLevels)],
    [levels.data, preview],
  );
  const venueName = venues.data?.find((item) => item.venue === venue)?.name ?? venue;

  useScanSnapshotUpdates(venue);

  return (
    <div className="space-y-6">
      <PageHeader
        back={
          <nav aria-label="Путь" className="text-sm text-muted">
            <Link to="/scanner" className="hover:text-link">
              Сканер
            </Link>
            {' / '}
            {venueName}
          </nav>
        }
        title={symbol}
        titleAction={
          row?.price ? (
            <span className="font-mono text-xl text-text-secondary">{row.price}</span>
          ) : null
        }
        meta={
          snapshot.data?.computedAt ? (
            <>
              Снимок <DateTime value={snapshot.data.computedAt} />
            </>
          ) : null
        }
        actions={row ? <ScanPairSummary row={row} /> : null}
      />
      {!row && !snapshot.isPending && (
        <p className="text-sm text-muted">Пары нет в последнем снимке сканера.</p>
      )}
      <div className="grid items-start gap-6 lg:grid-cols-[minmax(0,1fr)_20rem]">
        <div className="min-w-0 space-y-6">
          <Card
            title="График"
            actions={
              <div role="group" aria-label="Таймфрейм" className="flex flex-wrap gap-1">
                {scanChartTimeframes.map((item) => (
                  <Link
                    key={item}
                    to="/scanner/$venue/$symbol"
                    params={{ venue, symbol }}
                    search={{ timeframe: item }}
                    className={
                      item === timeframe
                        ? 'rounded-md border border-link px-2.5 py-1 text-sm text-link'
                        : 'rounded-md border border-line-strong px-2.5 py-1 text-sm text-text-secondary hover:text-text'
                    }
                  >
                    {item}
                  </Link>
                ))}
              </div>
            }
          >
            <div className="space-y-4">
              <ScanPairChart
                venue={venue}
                symbol={symbol}
                timeframe={timeframe}
                levels={chartLevels}
              />
              {levels.data ? (
                <ScanTimeframeLevels levels={levels.data} />
              ) : levels.isError ? (
                <ApiErrorMessage error={levels.error} fallback="Не удалось посчитать уровни." />
              ) : (
                <p className="text-sm text-muted">Загрузка уровней…</p>
              )}
            </div>
          </Card>
          <Card title="Идеи входа по всем таймфреймам">
            {ideas.data ? (
              <ScanTradeIdeasTable venue={venue} ideas={ideas.data} onPreview={setPreview} />
            ) : (
              <p className="text-sm text-muted">
                {ideas.isError ? 'Идеи не загрузились.' : 'Загрузка…'}
              </p>
            )}
          </Card>
          <ScanPairTrades venue={venue} symbol={symbol} />
        </div>
        <div className="space-y-6">
          <Card title="Настройки сделок">
            <ScanTradeSettingsForm venue={venue} />
          </Card>
          {row && (
            <details className="group rounded-lg border border-line bg-panel p-5">
              <summary className="cursor-pointer font-semibold">
                Все метрики <span className="text-sm font-normal text-muted">(уровни по 1h)</span>
              </summary>
              <div className="mt-4">
                <ScanRowMetrics row={row} />
              </div>
            </details>
          )}
        </div>
      </div>
    </div>
  );
}
