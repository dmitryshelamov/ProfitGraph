import {
  CandlestickSeries,
  ColorType,
  createChart,
  createSeriesMarkers,
  type IChartApi,
  type IPriceLine,
  type ISeriesApi,
  type ISeriesMarkersPluginApi,
  type LogicalRange,
  type Time,
  type UTCTimestamp,
} from 'lightweight-charts';
import { useEffect, useMemo, useRef } from 'react';

import {
  type ChartLevel,
  type ChartTrade,
  drawableCandles,
  type HistoryCandle,
  pricePrecision,
} from '../model/candleHistory';
import { drawableLive, type LiveCandle, withAlpha } from '../model/liveCandles';
import { markerPosition, tradeMarkers } from '../model/tradeMarkers';

/** How many candles the chart shows when it first opens. */
const initialBars = 120;

/** How close to the oldest loaded candle the view must come before older candles are asked for. */
const loadAheadBars = 10;

/** How see-through the candle that is not closed yet is drawn. */
const formingAlpha = 0.45;

/** Dashed, as a level that is not a trade yet. */
const dashedLine = 2;

/** Solid, as a price a proposed trade uses. */
const solidLine = 0;

function token(name: string, fallback: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback;
}

/**
 * Closed candles in UTC with trade markers outside their candles, labelled with the exact price, and dashed levels. Scrolling to the oldest loaded
 * candle calls `onReachStart`; candles added before it keep the view where it was. Live candles (the one being formed) are
 * drawn after the closed ones, the unclosed one see-through; a live candle the history already has is not drawn again.
 */
export function CandleHistoryChart({
  candles,
  trades,
  levels = [],
  live = [],
  candleSeconds,
  pricePlaces = null,
  label,
  onReachStart,
}: {
  candles: readonly HistoryCandle[];
  trades: readonly ChartTrade[];
  levels?: readonly ChartLevel[];
  live?: readonly LiveCandle[];
  /** The length of one candle; a trade is drawn only inside a candle's period. */
  candleSeconds: number;
  /** The instrument's decimal places for prices; when unknown they follow the candles' prices. */
  pricePlaces?: number | null;
  label: string;
  onReachStart?: () => void;
}) {
  const container = useRef<HTMLDivElement>(null);
  const chart = useRef<{
    api: IChartApi;
    candles: ISeriesApi<'Candlestick'>;
    markers: ISeriesMarkersPluginApi<Time>;
    lines: IPriceLine[];
  } | null>(null);
  const firstTime = useRef<number | null>(null);
  const liveBase = useRef<unknown>(null);
  const reachStart = useRef(onReachStart);
  const places = useMemo(() => pricePrecision(pricePlaces, candles), [pricePlaces, candles]);
  const drawn = useMemo(() => drawableCandles(candles), [candles]);
  const drawnLive = useMemo(() => drawableLive(drawn, live), [drawn, live]);
  const placed = useMemo(
    () => tradeMarkers(drawn, trades, candleSeconds),
    [drawn, trades, candleSeconds],
  );

  useEffect(() => {
    reachStart.current = onReachStart;
  }, [onReachStart]);

  useEffect(() => {
    if (!container.current) {
      return undefined;
    }

    const up = token('--color-up', '#3fd393');
    const down = token('--color-down', '#ff6b81');
    const api = createChart(container.current, {
      height: 360,
      autoSize: true,
      layout: {
        background: { type: ColorType.Solid, color: token('--color-panel', '#151a22') },
        textColor: token('--color-muted', '#8b94a6'),
      },
      grid: {
        vertLines: { color: token('--color-line', '#242b37') },
        horzLines: { color: token('--color-line', '#242b37') },
      },
      timeScale: { timeVisible: true, secondsVisible: false },
      localization: { locale: 'ru-RU' },
    });
    const series = api.addSeries(CandlestickSeries, {
      upColor: up,
      downColor: down,
      wickUpColor: up,
      wickDownColor: down,
      borderVisible: false,
    });
    const onRange = (range: LogicalRange | null) => {
      if (range && range.from < loadAheadBars) {
        reachStart.current?.();
      }
    };
    api.timeScale().subscribeVisibleLogicalRangeChange(onRange);
    chart.current = { api, candles: series, markers: createSeriesMarkers(series, []), lines: [] };

    return () => {
      api.timeScale().unsubscribeVisibleLogicalRangeChange(onRange);
      api.remove();
      chart.current = null;
      firstTime.current = null;
      liveBase.current = null;
    };
  }, []);

  useEffect(() => {
    chart.current?.candles.applyOptions({
      priceFormat: { type: 'price', precision: places, minMove: 10 ** -places },
    });
  }, [places]);

  useEffect(() => {
    const current = chart.current;

    if (!current) {
      return;
    }

    const scale = current.api.timeScale();
    const visible = scale.getVisibleLogicalRange();
    const previousFirst = firstTime.current;
    current.candles.setData(
      drawn.map((candle) => ({ ...candle, time: candle.time as UTCTimestamp })),
    );

    if (drawn.length > 0 && previousFirst === null) {
      scale.setVisibleLogicalRange({
        from: Math.max(0, drawn.length - initialBars),
        to: drawn.length + 2,
      });
    } else if (visible && previousFirst !== null) {
      // Older candles were added on the left: move the view by them so it stays on the same candles.
      const added = drawn.findIndex((candle) => candle.time >= previousFirst);

      if (added > 0) {
        scale.setVisibleLogicalRange({ from: visible.from + added, to: visible.to + added });
      }
    }

    firstTime.current = drawn[0]?.time ?? null;
  }, [drawn]);

  useEffect(() => {
    const current = chart.current;

    if (!current) {
      return;
    }

    // `setData` above dropped earlier live candles, so a new history draws them all again; otherwise only the newest changed.
    const redraw = liveBase.current !== drawn;
    liveBase.current = drawn;
    const up = token('--color-up', '#3fd393');
    const down = token('--color-down', '#ff6b81');

    for (const candle of redraw ? drawnLive : drawnLive.slice(-1)) {
      const colour = candle.close >= candle.open ? up : down;
      const style = candle.forming
        ? {
            color: withAlpha(colour, formingAlpha),
            wickColor: withAlpha(colour, formingAlpha),
            borderColor: withAlpha(colour, formingAlpha),
          }
        : {};
      current.candles.update({
        time: candle.time as UTCTimestamp,
        open: candle.open,
        high: candle.high,
        low: candle.low,
        close: candle.close,
        ...style,
      });
    }
  }, [drawn, drawnLive]);

  useEffect(() => {
    const up = token('--color-up', '#3fd393');
    const down = token('--color-down', '#ff6b81');
    chart.current?.markers.setMarkers(
      placed.map((trade) => ({
        id: trade.id,
        time: trade.time as UTCTimestamp,
        position: markerPosition(trade.side),
        shape: trade.side === 'Buy' ? ('arrowUp' as const) : ('arrowDown' as const),
        color: trade.side === 'Buy' ? up : down,
        text: trade.text,
      })),
    );
  }, [placed]);

  useEffect(() => {
    const current = chart.current;

    if (!current) {
      return;
    }

    const up = token('--color-up', '#3fd393');
    const down = token('--color-down', '#ff6b81');
    const neutral = token('--color-link', '#7aa7ff');

    for (const line of current.lines) {
      current.candles.removePriceLine(line);
    }

    current.lines = levels.map((level) =>
      current.candles.createPriceLine({
        price: Number(level.price),
        color: level.neutral ? neutral : level.side === 'Buy' ? up : down,
        lineStyle: level.solid ? solidLine : dashedLine,
        lineWidth: level.solid ? 2 : 1,
        axisLabelVisible: true,
        title: level.title,
      }),
    );
  }, [levels]);

  return (
    <figure
      ref={container}
      aria-label={label}
      data-candles={drawn.length}
      data-live={drawnLive.length}
      data-markers={placed.length}
      data-levels={levels.length}
      className="h-90 w-full overflow-hidden rounded-md border border-line"
    />
  );
}
