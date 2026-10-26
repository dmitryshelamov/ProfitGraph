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
} from '../model/candleHistory';
import { markerPosition, tradeMarkers } from '../model/tradeMarkers';

/** How many candles the chart shows when it first opens. */
const initialBars = 120;

/** How close to the oldest loaded candle the view must come before older candles are asked for. */
const loadAheadBars = 10;

/** Dashed, as a level that is not a trade yet. */
const dashedLine = 2;

function token(name: string, fallback: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback;
}

/**
 * Closed candles in UTC with trade markers whose arrow tips touch their prices and dashed levels. Scrolling to the oldest loaded
 * candle calls `onReachStart`; candles added before it keep the view where it was.
 */
export function CandleHistoryChart({
  candles,
  trades,
  levels = [],
  candleSeconds,
  label,
  onReachStart,
}: {
  candles: readonly HistoryCandle[];
  trades: readonly ChartTrade[];
  levels?: readonly ChartLevel[];
  /** The length of one candle; a trade is drawn only inside a candle's period. */
  candleSeconds: number;
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
  const reachStart = useRef(onReachStart);
  const drawn = useMemo(() => drawableCandles(candles), [candles]);
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
    };
  }, []);

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
    const up = token('--color-up', '#3fd393');
    const down = token('--color-down', '#ff6b81');
    chart.current?.markers.setMarkers(
      placed.map((trade) => ({
        id: trade.id,
        time: trade.time as UTCTimestamp,
        position: markerPosition(trade.side),
        price: trade.price,
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

    for (const line of current.lines) {
      current.candles.removePriceLine(line);
    }

    current.lines = levels.map((level) =>
      current.candles.createPriceLine({
        price: Number(level.price),
        color: level.side === 'Buy' ? up : down,
        lineStyle: dashedLine,
        lineWidth: 1,
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
      data-markers={placed.length}
      data-levels={levels.length}
      className="h-90 w-full overflow-hidden rounded-md border border-line"
    />
  );
}
