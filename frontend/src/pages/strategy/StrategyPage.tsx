import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getRouteApi, Link } from '@tanstack/react-router';
import { useState } from 'react';

import { StrategyRuns } from '@/modules/backtests';
import { StrategyOptimizations } from '@/modules/optimizations';
import {
  renameStrategy,
  strategyKeys,
  strategyQuery,
  strategyTemplateLabel,
} from '@/modules/strategies';
import { ApiError } from '@/shared/api/client';
import { Button, buttonClassName } from '@/shared/ui/Button';
import { Card } from '@/shared/ui/Card';
import { DateTime } from '@/shared/ui/DateTime';
import { PageHeader } from '@/shared/ui/PageHeader';
import { RenameForm } from '@/shared/ui/RenameForm';

import { StrategyParameters } from './StrategyParameters';
import { StrategyValidationCard } from './StrategyValidationCard';
import { StrategyVersions } from './StrategyVersions';

const route = getRouteApi('/_authenticated/_shell/strategies/$strategyId');

function loadFailure(error: Error): string {
  if (error instanceof ApiError && error.status === 404) {
    return 'Стратегия не найдена.';
  }

  if (error instanceof ApiError && error.code === 'unsupported_strategy_format') {
    return 'Стратегия сохранена в старом формате, её нужно создать заново.';
  }

  return 'Не удалось загрузить стратегию.';
}

export function StrategyPage() {
  const { strategyId } = route.useParams();
  const strategy = useQuery(strategyQuery(strategyId));
  const queryClient = useQueryClient();
  const [renaming, setRenaming] = useState(false);
  const rename = useMutation({
    mutationFn: (name: string) => renameStrategy(strategyId, name),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: strategyKeys.all }),
  });
  const back = (
    <nav aria-label="Путь" className="text-sm text-muted">
      <Link to="/strategies" className="hover:text-link">
        Стратегии
      </Link>
    </nav>
  );

  if (!strategy.data) {
    return (
      <div className="mx-auto max-w-6xl space-y-3">
        {back}
        {strategy.isError ? (
          <p role="alert" className="text-danger-text">
            {loadFailure(strategy.error)}
          </p>
        ) : (
          <p role="status">Загрузка стратегии…</p>
        )}
      </div>
    );
  }

  const current = strategy.data;

  return (
    <div className="mx-auto max-w-6xl">
      <PageHeader
        back={back}
        title={current.name}
        titleAction={
          <Button size="sm" variant="ghost" onClick={() => setRenaming(true)}>
            Переименовать
          </Button>
        }
        editor={
          renaming ? (
            <RenameForm
              initial={current.name}
              label="Название стратегии"
              maxLength={200}
              onSave={(name) => rename.mutateAsync(name)}
              onDone={() => setRenaming(false)}
            />
          ) : undefined
        }
        meta={
          <>
            {strategyTemplateLabel(current.definition.templateKind)} · версия{' '}
            {current.currentVersion} · сохранена <DateTime value={current.updatedAt} />
          </>
        }
        actions={
          <>
            <Link
              to="/strategies/$strategyId/backtest"
              params={{ strategyId }}
              className={buttonClassName()}
            >
              Запустить прогон
            </Link>
            <Link
              to="/strategies/$strategyId/optimize"
              params={{ strategyId }}
              className={buttonClassName()}
            >
              Оптимизировать
            </Link>
            <Link
              to="/strategies/$strategyId/edit"
              params={{ strategyId }}
              className={buttonClassName('primary')}
            >
              Открыть редактор
            </Link>
          </>
        }
      />
      <div className="grid gap-6 lg:grid-cols-[2fr_1fr]">
        <div className="space-y-6">
          <StrategyValidationCard strategyId={strategyId} version={current.currentVersion} />
          <StrategyParameters definition={current.definition} />
          <Card title="Прогоны">
            <StrategyRuns strategyId={strategyId} />
          </Card>
          <Card title="Оптимизации">
            <StrategyOptimizations strategyId={strategyId} />
          </Card>
        </div>
        <StrategyVersions strategyId={strategyId} currentVersion={current.currentVersion} />
      </div>
    </div>
  );
}
