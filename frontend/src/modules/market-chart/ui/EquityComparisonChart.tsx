import {
  ColorType,
  createChart,
  type IChartApi,
  LineSeries,
  type UTCTimestamp,
} from 'lightweight-charts';
import { useEffect, useRef } from 'react';

import { drawableEquity, type EquityPoint } from '../model/equityLine';

const palette = ['#7aa7ff', '#3fd393', '#f5b84a', '#c58bff', '#ff6b81', '#4dd0e1'];

function token(name: string, fallback: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback;
}

export interface EquityLine {
  readonly label: string;
  readonly points: readonly EquityPoint[];
}

/** Equity of several runs on one time scale, one colored line each, in UTC. */
export function EquityComparisonChart({
  lines,
  label,
}: {
  lines: readonly EquityLine[];
  label: string;
}) {
  const container = useRef<HTMLDivElement>(null);
  const chart = useRef<IChartApi | null>(null);

  useEffect(() => {
    if (!container.current) {
      return undefined;
    }

    chart.current = createChart(container.current, {
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
      timeScale: { timeVisible: true, secondsVisible: false },
      localization: { locale: 'ru-RU' },
    });

    return () => {
      chart.current?.remove();
      chart.current = null;
    };
  }, []);

  // Lines arrive as a new array on every render; redraw only when what they show changes.
  const drawn = useRef(lines);
  drawn.current = lines;
  const signature = lines
    .map((line) => `${line.label}:${line.points.length}:${line.points.at(-1)?.time ?? ''}`)
    .join('|');

  useEffect(() => {
    const api = chart.current;

    if (!api) {
      return undefined;
    }

    const series = drawn.current.map((line, index) => {
      const added = api.addSeries(LineSeries, {
        color: palette[index % palette.length],
        lineWidth: 2,
        title: line.label,
      });
      added.setData(
        drawableEquity(line.points).map((value) => ({
          ...value,
          time: value.time as UTCTimestamp,
        })),
      );

      return added;
    });
    api.timeScale().fitContent();

    return () => {
      // On unmount the chart is removed first and takes its series with it.
      if (chart.current !== api) {
        return;
      }

      for (const added of series) {
        api.removeSeries(added);
      }
    };
  }, [signature]);

  return (
    <figure
      ref={container}
      aria-label={label}
      data-lines={lines.length}
      className="h-80 w-full overflow-hidden rounded-md border border-line"
    />
  );
}
