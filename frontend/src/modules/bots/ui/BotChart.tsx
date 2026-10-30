import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo } from 'react';

import {
  CandleHistoryChart,
  type ChartLevel,
  type ChartTrade,
  timeframeSeconds,
  tooFarBehind,
} from '@/modules/market-chart';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { DateTime } from '@/shared/ui/DateTime';

import { botCandlesQuery, botKeys, botNewCandlesQuery, botOpenOrdersQuery } from '../api/botsApi';
import { mergeCandlePages } from '../model/botCandles';
import { atPlaces, type BotDisplay } from '../model/botDisplay';
import { formingNote } from '../model/formingCandle';
import { orderLevels } from '../model/orderLevels';
import type { Bot, BotCandlesPage } from '../model/types';
import { useFormingCandle } from '../model/useFormingCandle';

/** Why the venue could not be read, by the stable code of its history error. */
function unavailableText(code: string | null): string {
  switch (code) {
    case 'provider_unavailable':
      return 'площадка сейчас не отвечает';
    case 'historical_data_invalid_request':
      return 'площадка не знает этот инструмент';
    case 'invalid_provider_data':
      return 'площадка вернула неверные данные';
    default:
      return 'история площадки недоступна';
  }
}

/**
 * The bot's instrument's closed candles on a timeframe of its venue, the bot's own by default, its fills as markers on the
 * candles that contain them and its working limit orders as dashed levels. Scrolling left loads older candles; candles closed
 * since the page opened arrive every minute. The candle being formed follows the venue's live channel (ADR 0027) without being
 * stored; that channel carries the bot's own timeframe only, so another timeframe shows closed candles alone.
 */
export function BotChart({
  bot,
  display,
  timeframe = bot.timeframe,
}: {
  bot: Bot;
  display: BotDisplay;
  timeframe?: string;
}) {
  const queryClient = useQueryClient();
  const history = useInfiniteQuery(botCandlesQuery(bot.id, timeframe));
  const newestTo = history.data?.pages[0]?.to;
  const fresh = useQuery({
    ...botNewCandlesQuery(bot.id, timeframe, newestTo ?? ''),
    enabled: newestTo !== undefined,
  });
  const orders = useQuery(botOpenOrdersQuery(bot.id));
  const forming = useFormingCandle(bot.id);
  const own = timeframe === bot.timeframe;

  // Candles closed since the newest page are read in one request; once they no longer fit, the chart starts over.
  useEffect(() => {
    if (newestTo === undefined) {
      return undefined;
    }

    const timer = setInterval(() => {
      if (tooFarBehind(newestTo, new Date(), timeframe)) {
        void queryClient.resetQueries({
          queryKey: botKeys.candles(bot.id, timeframe),
          exact: true,
        });
      }
    }, 60_000);

    return () => clearInterval(timer);
  }, [bot.id, timeframe, newestTo, queryClient]);

  const pages = useMemo<BotCandlesPage[]>(
    () => [...(history.data?.pages ?? []), ...(fresh.data ? [fresh.data] : [])],
    [history.data, fresh.data],
  );
  const merged = useMemo(() => mergeCandlePages(pages), [pages]);
  const trades = useMemo<ChartTrade[]>(
    () =>
      merged.fills.map((fill) => ({
        id: fill.id,
        time: fill.filledAt,
        side: fill.side,
        price: fill.price,
        text: atPlaces(fill.price, display.pricePlaces),
      })),
    [merged.fills, display.pricePlaces],
  );
  const levels = useMemo<ChartLevel[]>(
    () => orderLevels(orders.data ?? [], display.pricePlaces),
    [orders.data, display.pricePlaces],
  );
  const loadEarlier = () => {
    if (history.hasNextPage && !history.isFetchingNextPage) {
      void history.fetchNextPage();
    }
  };
  const partial = pages.some((page) => page.status === 'Partial');
  const unavailable = pages.find((page) => page.status === 'Unavailable');
  const truncated = pages.some((page) => page.fillsTruncated);
  const oldest = merged.candles[0]?.openTime;

  return (
    <div className="space-y-3">
      <CandleHistoryChart
        key={timeframe}
        candles={merged.candles}
        trades={trades}
        levels={levels}
        live={own ? forming.live : []}
        candleSeconds={timeframeSeconds[timeframe] ?? 3600}
        pricePlaces={display.pricePlaces}
        label="Свечи бота"
        onReachStart={loadEarlier}
      />
      {own && formingNote(forming.feed) && (
        <p role="status" data-forming={forming.feed} className="text-sm text-muted">
          {formingNote(forming.feed)}
        </p>
      )}
      {!own && (
        <p className="text-sm text-muted">
          На {timeframe} показаны только закрытые свечи: формирующаяся свеча идёт по таймфрейму бота
          ({bot.timeframe}).
        </p>
      )}
      {history.isError && (
        <ApiErrorMessage error={history.error} fallback="Не удалось загрузить свечи." />
      )}
      {history.isPending && <p className="text-sm text-muted">Загрузка свечей…</p>}
      {history.isSuccess && merged.candles.length === 0 && (
        <p className="text-sm text-muted">За загруженный период свечей нет.</p>
      )}
      {partial && (
        <p className="text-sm text-text-secondary">
          В истории площадки есть интервалы без свечей, например часы или дни без продаж. Они
          пропущены и не считаются ошибкой.
        </p>
      )}
      {unavailable && (
        <p role="status" className="text-sm text-text-secondary">
          Показаны только сохранённые свечи: {unavailableText(unavailable.errorCode)}.
        </p>
      )}
      {truncated && (
        <p className="text-sm text-text-secondary">
          Сделок слишком много: на отрезке отмечены первые 1000.
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
        {history.isSuccess && !history.hasNextPage && <span>Более ранней истории нет.</span>}
      </div>
    </div>
  );
}
