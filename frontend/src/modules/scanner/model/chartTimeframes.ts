/** The timeframes a scanner pair's chart offers, shortest first. */
export const scanChartTimeframes = ['1m', '5m', '15m', '1h', '4h', '1d'] as const;

/** The timeframe a pair's chart opens with. */
export const defaultScanChartTimeframe = '15m';

/** A timeframe from the address if the chart offers it. */
export function scanChartTimeframe(value: unknown): string | undefined {
  return typeof value === 'string' && (scanChartTimeframes as readonly string[]).includes(value)
    ? value
    : undefined;
}
