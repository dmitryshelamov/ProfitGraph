import { useMemo, useState } from 'react';

import { formatMetric } from '../model/format';
import { scanScenarios, scenarioTotals } from '../model/scenarios';
import type { ScanSignal } from '../model/types';
import { ScanSignalsTable } from './ScanSignalsTable';

const selectClass =
  'rounded-md border border-line-strong bg-panel px-3 py-2 text-sm text-text outline-none focus:border-link';

/** A backtest's signals with a pair filter; a chosen pair also shows how each scenario did on it alone. */
export function ScanBacktestSignals({
  venue,
  signals,
}: {
  venue: string;
  signals: readonly ScanSignal[];
}) {
  const [pair, setPair] = useState('');
  const pairs = useMemo(
    () => [...new Set(signals.map((signal) => signal.symbol))].sort(),
    [signals],
  );
  const shown = useMemo(
    () => (pair === '' ? signals : signals.filter((signal) => signal.symbol === pair)),
    [pair, signals],
  );
  const totals = useMemo(() => (pair === '' ? [] : scenarioTotals(shown)), [pair, shown]);

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-3 text-sm">
        <select
          aria-label="Пара"
          className={selectClass}
          value={pair}
          onChange={(event) => setPair(event.target.value)}
        >
          <option value="">Все пары</option>
          {pairs.map((symbol) => (
            <option key={symbol} value={symbol}>
              {symbol}
            </option>
          ))}
        </select>
        <span className="text-muted">Сигналов: {shown.length}</span>
      </div>
      {totals.length > 0 && (
        <ul aria-label="Итоги пары" className="grid gap-1 text-sm sm:grid-cols-2">
          {totals.map((total) => (
            <li key={total.scenario}>
              {scanScenarios.find((item) => item.scenario === total.scenario)?.label}:{' '}
              {total.trades} сд., прибыльных {total.wins}, сумма{' '}
              {total.totalR === null ? '—' : `${formatMetric(total.totalR, 2)} R`}
            </li>
          ))}
        </ul>
      )}
      <ScanSignalsTable venue={venue} signals={shown} />
    </div>
  );
}
