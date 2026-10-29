import { Link } from '@tanstack/react-router';

import { DateTime } from '@/shared/ui/DateTime';

import { formatMetric } from '../model/format';
import { shownScenarios, tradeResult } from '../model/scenarios';
import type { ScanSignal } from '../model/types';

const scenarioShort = {
  Levels: 'уровни',
  LevelsRewardRisk2: 'R:R≥2',
  Target2R: '2R',
  Pullback: 'откат',
} as const;

function percent(value: string | null): string {
  return value === null ? '—' : formatMetric(value, 2);
}

/**
 * A backtest's signals, newest first: when, the pair (opening its chart), the entry price, the changes after it in percent and the
 * result in R of each scenario's trade.
 */
export function ScanSignalsTable({
  venue,
  signals,
}: {
  venue: string;
  signals: readonly ScanSignal[];
}) {
  const scenarios = shownScenarios(signals);

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-right text-sm">
        <thead className="text-text-secondary">
          <tr>
            <th className="py-2 pr-3 text-left">Время (UTC)</th>
            <th className="py-2 pr-3 text-left">Пара</th>
            <th className="py-2 pl-3">Вход</th>
            <th className="py-2 pl-3">15 мин, %</th>
            <th className="py-2 pl-3">1 ч, %</th>
            <th className="py-2 pl-3">4 ч, %</th>
            <th className="py-2 pl-3">24 ч, %</th>
            <th className="py-2 pl-3">Макс. рост, %</th>
            <th className="py-2 pl-3">Макс. просадка, %</th>
            {scenarios.map(({ scenario, direction, label }) => (
              <th key={`${direction}:${scenario}`} className="py-2 pl-3" title={label}>
                R: {direction === 'Short' ? 'шорт ' : ''}
                {scenarioShort[scenario]}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {signals.map((signal) => (
            <tr key={`${signal.symbol}-${signal.at}`} className="border-t border-line">
              <td className="py-1.5 pr-3 text-left whitespace-nowrap">
                <DateTime value={signal.at} />
              </td>
              <td className="py-1.5 pr-3 text-left">
                <Link
                  to="/scanner/$venue/$symbol"
                  params={{ venue, symbol: signal.symbol }}
                  className="font-medium hover:underline"
                >
                  {signal.symbol}
                </Link>
              </td>
              <td className="py-1.5 pl-3 font-mono">{formatMetric(signal.entryPrice, null)}</td>
              <td className="py-1.5 pl-3 font-mono">{percent(signal.change15m)}</td>
              <td className="py-1.5 pl-3 font-mono">{percent(signal.change1h)}</td>
              <td className="py-1.5 pl-3 font-mono">{percent(signal.change4h)}</td>
              <td className="py-1.5 pl-3 font-mono">{percent(signal.change24h)}</td>
              <td className="py-1.5 pl-3 font-mono">{percent(signal.maxRise24h)}</td>
              <td className="py-1.5 pl-3 font-mono">{percent(signal.maxDrop24h)}</td>
              {scenarios.map(({ scenario, direction }) => (
                <td key={`${direction}:${scenario}`} className="py-1.5 pl-3 font-mono">
                  {percent(tradeResult(signal, scenario, direction))}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
