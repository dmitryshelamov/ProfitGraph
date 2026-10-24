import { useQuery } from '@tanstack/react-query';

import { Amount } from '@/modules/backtests';
import { parameterReadableName } from '@/modules/strategies';
import { Card } from '@/shared/ui/Card';
import { DateTime } from '@/shared/ui/DateTime';

import { optimizationWindowsQuery } from '../api/optimizationsApi';
import { windowStatusLabel } from '../model/format';
import type { WalkForwardSummary } from '../model/types';

function Summary({ summary }: { summary: WalkForwardSummary }) {
  return (
    <dl className="grid gap-3 text-sm sm:grid-cols-4">
      <div>
        <dt className="text-muted">Прибыль на проверках</dt>
        <dd>
          <Amount value={summary.testTotalPnl} />
        </dd>
      </div>
      <div>
        <dt className="text-muted">Доходность на проверках, %</dt>
        <dd>
          <Amount value={summary.testCompoundedReturnPercent} />
        </dd>
      </div>
      <div>
        <dt className="text-muted">Прибыльных окон</dt>
        <dd className="font-mono">
          {summary.profitableWindows} из {summary.testWindows}
        </dd>
      </div>
      <div>
        <dt className="text-muted">Эффективность (WFE)</dt>
        <dd>
          <Amount value={summary.overallWfe} />
        </dd>
      </div>
      {summary.parameterStability.map((stability) => (
        <div key={stability.parameter} className="sm:col-span-2">
          <dt className="text-muted">
            {parameterReadableName(stability.parameter)}: победители по окнам
          </dt>
          <dd className="font-mono">
            {stability.winners.map((winner) => winner.value).join(' → ')}
            {stability.rangeInSteps !== null && ` (разброс ${stability.rangeInSteps} шаг.)`}
          </dd>
        </div>
      ))}
    </dl>
  );
}

/** Walk-forward windows (ADR 0014): each train range's winner and how it did on the next unseen range. */
export function WalkForwardWindows({
  optimizationId,
  summary,
}: {
  optimizationId: string;
  summary: WalkForwardSummary | null | undefined;
}) {
  const windows = useQuery(optimizationWindowsQuery(optimizationId));

  return (
    <Card title="Скользящие окна">
      <div className="space-y-4">
        {summary && <Summary summary={summary} />}
        {windows.isError && (
          <p role="alert" className="text-danger-text">
            Не удалось загрузить окна.
          </p>
        )}
        {windows.data && windows.data.length === 0 && (
          <p className="text-muted">Ни одно окно ещё не завершено.</p>
        )}
        {windows.data && windows.data.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full text-sm" aria-label="Окна">
              <thead>
                <tr className="text-left text-muted">
                  <th className="py-2 pr-4 font-normal">Окно</th>
                  <th className="py-2 pr-4 font-normal">Проверка</th>
                  <th className="py-2 pr-4 font-normal">Итог</th>
                  <th className="py-2 pr-4 font-normal">Победитель</th>
                  <th className="py-2 pr-4 font-normal">Обучение, %</th>
                  <th className="py-2 pr-4 font-normal">Проверка, %</th>
                  <th className="py-2 pr-4 font-normal">WFE</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-line">
                {windows.data.map((window) => (
                  <tr key={window.index}>
                    <td className="py-2 pr-4 font-mono">{window.index + 1}</td>
                    <td className="py-2 pr-4">
                      <DateTime value={window.testFrom} /> — <DateTime value={window.testTo} />
                    </td>
                    <td className="py-2 pr-4">{windowStatusLabel(window.status)}</td>
                    <td className="py-2 pr-4">
                      {window.parameters
                        ? Object.entries(window.parameters)
                            .map(([name, value]) => `${parameterReadableName(name)} ${value}`)
                            .join(', ')
                        : '—'}
                    </td>
                    <td className="py-2 pr-4">
                      <Amount value={window.trainMetrics?.returnPercent} />
                    </td>
                    <td className="py-2 pr-4">
                      <Amount value={window.testMetrics?.returnPercent} />
                    </td>
                    <td className="py-2 pr-4">
                      <Amount value={window.wfe} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </Card>
  );
}
