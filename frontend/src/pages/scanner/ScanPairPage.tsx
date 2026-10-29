import { useQuery } from '@tanstack/react-query';
import { getRouteApi, Link } from '@tanstack/react-router';

import {
  defaultScanChartTimeframe,
  scanChartTimeframes,
  scanLevelsQuery,
  ScanPairChart,
  ScanRowMetrics,
  scanSnapshotQuery,
  ScanTimeframeLevels,
  ScanTradePanel,
  timeframeLevels,
  useScanSnapshotUpdates,
} from '@/modules/scanner';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Card } from '@/shared/ui/Card';
import { DateTime } from '@/shared/ui/DateTime';
import { PageHeader } from '@/shared/ui/PageHeader';

const route = getRouteApi('/_authenticated/_shell/scanner/$venue/$symbol');

/**
 * A scanner pair (ADR 0059): its chart on a chosen timeframe with that timeframe's support, resistance and reward-to-risk,
 * and its metrics from the latest snapshot, which stay on hours.
 */
export function ScanPairPage() {
  const { venue, symbol } = route.useParams();
  const { timeframe = defaultScanChartTimeframe } = route.useSearch();
  const snapshot = useQuery(scanSnapshotQuery(venue));
  const row = snapshot.data?.rows.find((item) => item.symbol === symbol);
  const levels = useQuery(scanLevelsQuery(venue, symbol, timeframe));

  useScanSnapshotUpdates(venue);

  return (
    <div className="space-y-6">
      <PageHeader
        title={symbol}
        meta={
          <Link to="/scanner" className="text-link hover:underline">
            ← Сканер
          </Link>
        }
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
      />
      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_18rem]">
        <Card>
          <ScanPairChart
            venue={venue}
            symbol={symbol}
            timeframe={timeframe}
            levels={timeframeLevels(levels.data)}
          />
        </Card>
        <div className="space-y-6">
          <Card title={`Уровни ${timeframe}`}>
            {levels.data ? (
              <ScanTimeframeLevels levels={levels.data} />
            ) : levels.isError ? (
              <ApiErrorMessage error={levels.error} fallback="Не удалось посчитать уровни." />
            ) : (
              <p className="text-sm text-muted">Загрузка…</p>
            )}
          </Card>
          <Card title="Метрики (уровни по 1h)">
            {row ? (
              <div className="space-y-3">
                <ScanRowMetrics row={row} />
                {snapshot.data?.computedAt && (
                  <p className="text-xs text-muted">
                    Снимок <DateTime value={snapshot.data.computedAt} />
                  </p>
                )}
              </div>
            ) : (
              <p className="text-sm text-muted">
                {snapshot.isPending ? 'Загрузка…' : 'Пары нет в последнем снимке сканера.'}
              </p>
            )}
          </Card>
        </div>
      </div>
      <Card title="Идеи входа по всем таймфреймам">
        <ScanTradePanel venue={venue} symbol={symbol} />
      </Card>
    </div>
  );
}
