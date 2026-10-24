import type { ReactNode } from 'react';

import type { BacktestRun } from '../model/types';
import { Amount } from './Amount';

function Kpi({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="rounded-lg border border-line bg-panel p-4">
      <dt className="text-sm text-muted">{label}</dt>
      <dd className="mt-1 text-xl">{children}</dd>
    </div>
  );
}

/** The whole period at a glance: profit, return, drawdown, trades and final equity in the quote currency. */
export function RunKpis({ run }: { run: BacktestRun }) {
  const full = run.result?.metrics?.full;
  const quote = run.history ? ` ${run.history.quoteAsset}` : '';

  return (
    <dl className="grid gap-4 sm:grid-cols-3 lg:grid-cols-5" aria-label="Итоги прогона">
      <Kpi label="Прибыль">
        <Amount value={run.result?.totalPnl} suffix={quote} />
      </Kpi>
      <Kpi label="Доходность">
        <Amount value={full?.returnPercent} suffix="%" />
      </Kpi>
      <Kpi label="Макс. просадка">
        <Amount value={full?.maxDrawdownPercent} suffix="%" />
      </Kpi>
      <Kpi label="Исполнений">
        <span className="font-mono">{full?.executions ?? '—'}</span>
      </Kpi>
      <Kpi label="Капитал в конце">
        <Amount value={run.result?.equity} suffix={quote} />
      </Kpi>
    </dl>
  );
}
