import { useQuery } from '@tanstack/react-query';
import { getRouteApi, Link } from '@tanstack/react-router';
import { useState } from 'react';

import { BacktestSetupForm } from '@/modules/backtests';
import { type BackgroundJob, JobProgress } from '@/modules/jobs';
import { strategyQuery } from '@/modules/strategies';
import { Button } from '@/shared/ui/Button';
import { Card } from '@/shared/ui/Card';
import { PageHeader } from '@/shared/ui/PageHeader';

const route = getRouteApi('/_authenticated/_shell/strategies/$strategyId/backtest');

export function BacktestSetupPage() {
  const { strategyId } = route.useParams();
  const strategy = useQuery(strategyQuery(strategyId));
  const [started, setStarted] = useState<BackgroundJob | null>(null);
  const back = (
    <nav aria-label="Путь" className="text-sm text-muted">
      <Link to="/strategies" className="hover:text-link">
        Стратегии
      </Link>
      {strategy.data && (
        <>
          {' / '}
          <Link to="/strategies/$strategyId" params={{ strategyId }} className="hover:text-link">
            {strategy.data.name}
          </Link>
        </>
      )}
    </nav>
  );

  if (!strategy.data) {
    return (
      <div className="mx-auto max-w-4xl space-y-3">
        {back}
        {strategy.isError ? (
          <p role="alert" className="text-danger-text">
            Не удалось загрузить стратегию.
          </p>
        ) : (
          <p role="status">Загрузка стратегии…</p>
        )}
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-4xl">
      <PageHeader
        back={back}
        title="Новый прогон"
        meta={`${strategy.data.name}, версия ${strategy.data.currentVersion}`}
      />
      {started ? (
        <Card
          title="Прогон поставлен в очередь"
          actions={
            <Button size="sm" onClick={() => setStarted(null)}>
              Настроить ещё один
            </Button>
          }
        >
          <JobProgress jobId={started.id} label="Прогон" />
        </Card>
      ) : (
        <BacktestSetupForm
          key={strategy.data.currentVersion}
          strategy={strategy.data}
          onStarted={setStarted}
        />
      )}
    </div>
  );
}
