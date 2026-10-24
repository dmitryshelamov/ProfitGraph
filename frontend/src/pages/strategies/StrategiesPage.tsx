import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';

import { Amount } from '@/modules/backtests';
import { jobStatusLabel } from '@/modules/jobs';
import { strategiesQuery, strategyTemplateLabel } from '@/modules/strategies';
import { Button } from '@/shared/ui/Button';
import { Card } from '@/shared/ui/Card';
import { DateTime } from '@/shared/ui/DateTime';
import { EmptyState } from '@/shared/ui/EmptyState';
import { PageHeader } from '@/shared/ui/PageHeader';

import { CreateFromTemplate } from './CreateFromTemplate';

const allTemplates = 'all';

export function StrategiesPage() {
  const strategies = useQuery(strategiesQuery());
  const [creating, setCreating] = useState(false);
  const [search, setSearch] = useState('');
  const [kind, setKind] = useState<string>(allTemplates);
  const kinds = [
    ...new Set(
      strategies.data?.flatMap((strategy) =>
        strategy.templateKind ? [strategy.templateKind] : [],
      ) ?? [],
    ),
  ];
  const term = search.trim().toLocaleLowerCase('ru-RU');
  const visible = strategies.data?.filter(
    (strategy) =>
      strategy.name.toLocaleLowerCase('ru-RU').includes(term) &&
      (kind === allTemplates || strategy.templateKind === kind),
  );

  return (
    <div className="mx-auto max-w-6xl">
      <PageHeader
        title={
          <>
            Стратегии{' '}
            <span className="ml-1 text-sm font-normal text-muted">
              {strategies.data?.length ?? '—'}
            </span>
          </>
        }
        actions={
          <Button
            variant="primary"
            aria-expanded={creating}
            onClick={() => setCreating((value) => !value)}
          >
            Новая стратегия
          </Button>
        }
      />
      {creating && <CreateFromTemplate />}
      <section aria-label="Сохранённые стратегии" className="space-y-4">
        <div className="flex flex-wrap items-center gap-2">
          <input
            type="search"
            aria-label="Поиск по названию"
            placeholder="Поиск по названию"
            className="w-72 max-w-full rounded-md border border-line-strong bg-panel px-3 py-2 outline-none placeholder:text-muted focus:border-link"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />
          {[allTemplates, ...kinds].map((item) => (
            <button
              key={item}
              type="button"
              aria-pressed={kind === item}
              className={`rounded-full border px-3 py-1 text-xs ${kind === item ? 'border-line-strong bg-selected text-text' : 'border-line-strong text-text-secondary hover:text-text'}`}
              onClick={() => setKind(item)}
            >
              {item === allTemplates ? 'Все' : strategyTemplateLabel(item)}
            </button>
          ))}
        </div>
        {strategies.isPending && <p role="status">Загрузка стратегий…</p>}
        {strategies.isError && (
          <p role="alert" className="text-danger-text">
            Не удалось загрузить стратегии: {strategies.error.message}
          </p>
        )}
        {strategies.data?.length === 0 && (
          <EmptyState
            action={
              !creating && <Button onClick={() => setCreating(true)}>Создать из шаблона</Button>
            }
          >
            Стратегий пока нет
          </EmptyState>
        )}
        {strategies.data && strategies.data.length > 0 && visible?.length === 0 && (
          <EmptyState>Ничего не найдено</EmptyState>
        )}
        {visible && visible.length > 0 && (
          <Card className="overflow-x-auto p-0">
            <table className="w-full min-w-[640px] text-left text-sm">
              <thead className="border-b border-line text-muted">
                <tr>
                  <th scope="col" className="px-4 py-3 font-medium">
                    Название
                  </th>
                  <th scope="col" className="px-4 py-3 font-medium">
                    Шаблон
                  </th>
                  <th scope="col" className="px-4 py-3 font-medium">
                    Версия
                  </th>
                  <th scope="col" className="px-4 py-3 font-medium">
                    Изменена
                  </th>
                  <th scope="col" className="px-4 py-3 font-medium">
                    Последний прогон
                  </th>
                </tr>
              </thead>
              <tbody>
                {visible.map((strategy) => (
                  <tr key={strategy.id} className="border-b border-line last:border-0">
                    <td className="px-4 py-3">
                      <Link
                        to="/strategies/$strategyId"
                        params={{ strategyId: strategy.id }}
                        className="font-medium hover:text-link"
                      >
                        {strategy.name}
                      </Link>
                    </td>
                    <td className="px-4 py-3 text-text-secondary">
                      {strategyTemplateLabel(strategy.templateKind)}
                    </td>
                    <td className="px-4 py-3 text-text-secondary">v{strategy.currentVersion}</td>
                    <td className="px-4 py-3 text-muted">
                      <DateTime value={strategy.updatedAt} />
                    </td>
                    <td className="px-4 py-3">
                      {strategy.latestRun ? (
                        <Link
                          to="/backtests/$runId"
                          params={{ runId: strategy.latestRun.id }}
                          className="hover:text-link"
                        >
                          {strategy.latestRun.returnPercent !== null ? (
                            <Amount value={strategy.latestRun.returnPercent} suffix="%" />
                          ) : (
                            jobStatusLabel(strategy.latestRun.status)
                          )}
                        </Link>
                      ) : (
                        <span className="text-muted">—</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Card>
        )}
      </section>
    </div>
  );
}
