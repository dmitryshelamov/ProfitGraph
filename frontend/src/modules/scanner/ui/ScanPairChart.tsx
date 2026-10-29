import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo } from 'react';

import {
  CandleHistoryChart,
  type ChartLevel,
  mergeCandles,
  timeframeSeconds,
  tooFarBehind,
} from '@/modules/market-chart';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { DateTime } from '@/shared/ui/DateTime';

import { scanCandlesQuery, scannerKeys, scanNewCandlesQuery } from '../api/scannerApi';
import type { ScanCandlesPage } from '../model/types';

/**
 * A scanner pair's closed candles on the chosen timeframe from the venue's public history. Scrolling left loads older
 * candles; candles closed since the page opened arrive every minute. The nearest support and resistance are dashed levels.
 */
export function ScanPairChart({
  venue,
  symbol,
  timeframe,
  levels = [],
}: {
  venue: string;
  symbol: string;
  timeframe: string;
  levels?: readonly ChartLevel[];
}) {
  const queryClient = useQueryClient();
  const history = useInfiniteQuery(scanCandlesQuery(venue, symbol, timeframe));
  const newestTo = history.data?.pages[0]?.to;
  const fresh = useQuery({
    ...scanNewCandlesQuery(venue, symbol, timeframe, newestTo ?? ''),
    enabled: newestTo !== undefined,
  });

  // Candles closed since the newest page are read in one request; once they no longer fit, the chart starts over.
  useEffect(() => {
    if (newestTo === undefined) {
      return undefined;
    }

    const timer = setInterval(() => {
      if (tooFarBehind(newestTo, new Date(), timeframe)) {
        void queryClient.resetQueries({
          queryKey: scannerKeys.candles(venue, symbol, timeframe),
          exact: true,
        });
      }
    }, 60_000);

    return () => clearInterval(timer);
  }, [venue, symbol, timeframe, newestTo, queryClient]);

  const pages = useMemo<ScanCandlesPage[]>(
    () => [...(history.data?.pages ?? []), ...(fresh.data ? [fresh.data] : [])],
    [history.data, fresh.data],
  );
  const candles = useMemo(() => mergeCandles(pages), [pages]);
  const loadEarlier = () => {
    if (history.hasNextPage && !history.isFetchingNextPage) {
      void history.fetchNextPage();
    }
  };
  const unavailable = pages.some((page) => page.status === 'Unavailable');
  const oldest = candles[0]?.openTime;

  return (
    <div className="space-y-3">
      <CandleHistoryChart
        key={timeframe}
        candles={candles}
        trades={[]}
        levels={levels}
        candleSeconds={timeframeSeconds[timeframe] ?? 3600}
        label={`Свечи ${symbol}`}
        onReachStart={loadEarlier}
      />
      {history.isError && (
        <ApiErrorMessage error={history.error} fallback="Не удалось загрузить свечи." />
      )}
      {history.isPending && <p className="text-sm text-muted">Загрузка свечей…</p>}
      {history.isSuccess && candles.length === 0 && (
        <p className="text-sm text-muted">За загруженный период свечей нет.</p>
      )}
      {unavailable && (
        <p role="status" className="text-sm text-text-secondary">
          Площадка сейчас не отвечает: показаны только сохранённые свечи.
        </p>
      )}
      <div className="flex flex-wrap items-center gap-3 text-sm text-muted">
        <Button
          size="sm"
          onClick={loadEarlier}
          disabled={!history.hasNextPage || history.isFetchingNextPage}
        >
          {history.isFetchingNextPage ? 'Загрузка…' : 'Загрузить раньше'}
        </Button>
        {oldest && (
          <span>
            С <DateTime value={oldest} />
          </span>
        )}
      </div>
    </div>
  );
}
