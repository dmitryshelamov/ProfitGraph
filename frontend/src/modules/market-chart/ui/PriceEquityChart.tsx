import {
  CandlestickSeries,
  ColorType,
  createChart,
  type IChartApi,
  type ISeriesApi,
  LineSeries,
  type UTCTimestamp,
} from 'lightweight-charts';
import { useEffect, useRef } from 'react';

import { drawable, type PricePoint } from '../model/pricePoint';

function token(name: string, fallback: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback;
}

/** Price candles on the right scale and account equity on the left, in UTC. */
export function PriceEquityChart({
  points,
  label,
}: {
  points: readonly PricePoint[];
  label: string;
}) {
  const container = useRef<HTMLDivElement>(null);
  const chart = useRef<{
    api: IChartApi;
    candles: ISeriesApi<'Candlestick'>;
    equity: ISeriesApi<'Line'>;
  } | null>(null);

  useEffect(() => {
    if (!container.current) {
      return undefined;
    }

    const up = token('--color-up', '#3fd393');
    const down = token('--color-down', '#ff6b81');
    const api = createChart(container.current, {
      height: 320,
      autoSize: true,
      layout: {
        background: { type: ColorType.Solid, color: token('--color-panel', '#151a22') },
        textColor: token('--color-muted', '#8b94a6'),
      },
      grid: {
        vertLines: { color: token('--color-line', '#242b37') },
        horzLines: { color: token('--color-line', '#242b37') },
      },
      leftPriceScale: { visible: true },
      timeScale: { timeVisible: true, secondsVisible: false },
      localization: { locale: 'ru-RU' },
    });
    chart.current = {
      api,
      candles: api.addSeries(CandlestickSeries, {
        upColor: up,
        downColor: down,
        wickUpColor: up,
        wickDownColor: down,
        borderVisible: false,
      }),
      equity: api.addSeries(LineSeries, {
        color: token('--color-link', '#7aa7ff'),
        lineWidth: 2,
        priceScaleId: 'left',
        title: 'Капитал',
      }),
    };

    return () => {
      api.remove();
      chart.current = null;
    };
  }, []);

  useEffect(() => {
    const { candles, equity } = drawable(points);
    chart.current?.candles.setData(
      candles.map((candle) => ({ ...candle, time: candle.time as UTCTimestamp })),
    );
    chart.current?.equity.setData(
      equity.map((value) => ({ ...value, time: value.time as UTCTimestamp })),
    );
    chart.current?.api.timeScale().fitContent();
  }, [points]);

  return (
    <figure
      ref={container}
      aria-label={label}
      data-points={points.length}
      className="h-80 w-full overflow-hidden rounded-md border border-line"
    />
  );
}
