import { useQuery } from '@tanstack/react-query';
import { getRouteApi, Link, useNavigate } from '@tanstack/react-router';

import { OptimizationSetupForm } from '@/modules/optimizations';
import { strategyQuery } from '@/modules/strategies';
import { PageHeader } from '@/shared/ui/PageHeader';

const route = getRouteApi('/_authenticated/_shell/strategies/$strategyId/optimize');

/** Sets up a parameter search of the strategy's current version and opens it once queued. */
export function OptimizationSetupPage() {
  const { strategyId } = route.useParams();
  const strategy = useQuery(strategyQuery(strategyId));
  const navigate = useNavigate();
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
        title="Новая оптимизация"
        meta={`${strategy.data.name}, версия ${strategy.data.currentVersion}`}
      />
      <OptimizationSetupForm
        key={strategy.data.currentVersion}
        strategy={strategy.data}
        onStarted={(job) =>
          void navigate({
            to: '/optimizations/$optimizationId',
            params: { optimizationId: job.id },
          })
        }
      />
    </div>
  );
}
