import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useState } from 'react';

import {
  createStrategy,
  strategyKeys,
  type StrategyTemplate,
  templatesQuery,
} from '@/modules/strategies';
import { Button } from '@/shared/ui/Button';
import { Card } from '@/shared/ui/Card';
import { Field } from '@/shared/ui/Field';
import { Input } from '@/shared/ui/Input';

/** Must match the server's strategy name limit. */
const maxNameLength = 200;

export function CreateFromTemplate() {
  const templates = useQuery(templatesQuery());
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [template, setTemplate] = useState<StrategyTemplate | null>(null);
  const [name, setName] = useState('');
  const create = useMutation({
    mutationFn: (selected: StrategyTemplate) =>
      createStrategy(name.trim(), selected.definition, selected.layout),
    onSuccess: async (strategy) => {
      queryClient.setQueryData(strategyKeys.detail(strategy.id), strategy);
      await queryClient.invalidateQueries({ queryKey: strategyKeys.all, exact: true });
      await navigate({ to: '/strategies/$strategyId', params: { strategyId: strategy.id } });
    },
  });

  return (
    <Card title="Создать из шаблона" className="mb-6">
      {templates.isPending && <p role="status">Загрузка шаблонов…</p>}
      {templates.isError && (
        <p role="alert" className="text-danger-text">
          Не удалось загрузить шаблоны: {templates.error.message}
        </p>
      )}
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {templates.data?.map((item) => (
          <button
            key={item.kind}
            type="button"
            aria-pressed={template?.kind === item.kind}
            className={`rounded-md border p-3 text-left hover:border-muted ${template?.kind === item.kind ? 'border-link bg-selected' : 'border-line-strong bg-panel-2'}`}
            onClick={() => {
              setTemplate(item);
              setName(item.name);
              create.reset();
            }}
          >
            <span className="block font-semibold">{item.name}</span>
            <span className="text-sm text-muted">{item.description}</span>
          </button>
        ))}
      </div>
      {template && (
        <form
          className="mt-4 flex flex-wrap items-end gap-3"
          onSubmit={(event) => {
            event.preventDefault();
            create.mutate(template);
          }}
        >
          <Field label="Название стратегии" className="w-80 max-w-full">
            <Input
              required
              maxLength={maxNameLength}
              value={name}
              onChange={(event) => setName(event.target.value)}
            />
          </Field>
          <Button type="submit" variant="primary" disabled={create.isPending || !name.trim()}>
            {create.isPending ? 'Создание…' : 'Создать стратегию'}
          </Button>
          {create.isError && (
            <p role="alert" className="w-full text-sm text-danger-text">
              Не удалось создать стратегию: {create.error.message}
            </p>
          )}
        </form>
      )}
    </Card>
  );
}
