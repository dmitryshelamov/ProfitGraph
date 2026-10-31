import { useQuery, useQueryClient } from '@tanstack/react-query';
import { getRouteApi, Link } from '@tanstack/react-router';
import { useEffect } from 'react';

import { isJobFinished, JobProgress, jobQuery } from '@/modules/jobs';
import {
  scanBacktestQuery,
  ScanBacktestSignals,
  ScanBacktestSummary,
  ScanCardCalibration,
  scannerKeys,
  ScanScenarioTable,
} from '@/modules/scanner';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Card } from '@/shared/ui/Card';
import { DateTime } from '@/shared/ui/DateTime';
import { PageHeader } from '@/shared/ui/PageHeader';

const route = getRouteApi('/_authenticated/_shell/scanner/backtests/$backtestId');

/**
 * A scanner backtest (ADR 0059): its job's progress, then what the price did after the preset's signals over the last 30 days and
 * the signals themselves.
 */
export function ScanBacktestPage() {
  const { backtestId } = route.useParams();
  const queryClient = useQueryClient();
  const backtest = useQuery(scanBacktestQuery(backtestId));
  const job = useQuery({
    ...jobQuery(backtest.data?.jobId ?? ''),
    enabled: !!backtest.data && !backtest.data.completedAt,
  });

  // The result is stored right before the job finishes, so a finished job means the backtest can be read once more.
  useEffect(() => {
    if (job.data && isJobFinished(job.data)) {
      void queryClient.invalidateQueries({ queryKey: scannerKeys.backtest(backtestId) });
    }
  }, [job.data, backtestId, queryClient]);

  if (backtest.isError) {
    return <ApiErrorMessage error={backtest.error} fallback="Не удалось загрузить проверку." />;
  }

  if (!backtest.data) {
    return <p className="text-muted">Загрузка…</p>;
  }

  const current = backtest.data;
  const spread = current.conditions.some((condition) => condition.metric === 'Spread');

  return (
    <div className="space-y-6">
      <PageHeader
        title={`Проверка пресета «${current.presetName}»`}
        meta={
          <span className="flex flex-wrap gap-x-3">
            <Link to="/scanner" className="text-link hover:underline">
              ← Сканер
            </Link>
            <span>
              Период <DateTime value={current.from} /> — <DateTime value={current.to} />
            </span>
          </span>
        }
      />
      {!current.completedAt && (
        <Card title="Ход проверки">
          <p className="mb-3 text-sm text-muted">
            Свечи всех пар загружаются с биржи по очереди, обычно это занимает 15–40 минут. Страницу
            можно закрыть: проверка продолжится и появится в списке на странице сканера.
          </p>
          <JobProgress jobId={current.jobId} label="Проверка пресета" />
        </Card>
      )}
      {current.summary && (
        <Card title={`Сигналов: ${current.summary.signals}`}>
          <div className="space-y-3">
            <p className="text-sm text-text-secondary">
              Сигнал — пара вошла в результат пресета (не чаще раза в час на пару, как у алертов).
              Вход по открытию следующей минуты, без комиссий. Проверено пар: {current.pairs}.
              {current.failedPairs && current.failedPairs.length > 0 && (
                <>
                  {' '}
                  История не загрузилась у {current.failedPairs.length}:{' '}
                  {current.failedPairs.join(', ')}.
                </>
              )}
              {spread && ' Условие на спред не проверялось: в истории нет стакана.'}
            </p>
            <ScanBacktestSummary summary={current.summary} />
          </div>
        </Card>
      )}
      {current.summary && current.summary.scenarios.length > 0 && (
        <Card title="Сценарии сделок">
          <div className="space-y-3">
            <p className="text-sm text-text-secondary">
              После каждого сигнала моделируется сделка: стоп у поддержки или под импульсом, цель у
              сопротивления или 2R, выход по стопу, цели или через 24 часа. R — расстояние от входа
              до стопа; результат после комиссии 0.1% на вход и выход. Если в одной минуте задеты и
              стоп, и цель, считается стоп.
            </p>
            <ScanScenarioTable scenarios={current.summary.scenarios} />
          </div>
        </Card>
      )}
      {current.summary?.calibration && (
        <Card title="Точность шансов карточек">
          <ScanCardCalibration calibration={current.summary.calibration} />
        </Card>
      )}
      {current.signals && current.signals.length > 0 && (
        <Card
          title={
            current.signalsTruncated ? `Последние ${current.signals.length} сигналов` : 'Сигналы'
          }
        >
          <ScanBacktestSignals
            venue={current.venue}
            signals={current.signals}
            preset={current.presetName}
          />
        </Card>
      )}
    </div>
  );
}
