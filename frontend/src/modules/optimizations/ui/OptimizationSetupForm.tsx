import { useMutation } from '@tanstack/react-query';
import { type FormEvent, type ReactElement, type ReactNode, useState } from 'react';

import type { BackgroundJob } from '@/modules/jobs';
import { SeriesFields } from '@/modules/market-data';
import { isPercentageParameter, parameterReadableName, type Strategy } from '@/modules/strategies';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { Card } from '@/shared/ui/Card';
import { Field } from '@/shared/ui/Field';
import { Input } from '@/shared/ui/Input';

import { startOptimization } from '../api/optimizationsApi';
import { methodLabel, objectiveLabel } from '../model/format';
import {
  type OptimizationMethod,
  type OptimizationObjective,
  optimizationSetupDefaults,
  type OptimizationSetupErrors,
  optimizationSetupRequest,
  type OptimizationSetupValues,
  type ParameterRange,
} from '../model/optimizationSetup';

const selectClassName =
  'w-full rounded-md border border-line-strong bg-panel px-3 py-2 text-text outline-none focus:border-link';
const methods: readonly OptimizationMethod[] = ['auto', 'grid', 'random', 'coarse_to_fine', 'tpe'];
const objectives: readonly OptimizationObjective[] = [
  'in_sample_return_percent',
  'in_sample_total_pnl',
  'in_sample_return_to_drawdown',
];

function Checked({
  label,
  error,
  children,
}: {
  label: ReactNode;
  error?: string;
  children: ReactElement<{ id?: string }>;
}) {
  return (
    <div>
      <Field label={label}>{children}</Field>
      {error && (
        <p role="alert" className="mt-1 text-sm text-danger-text">
          {error}
        </p>
      )}
    </div>
  );
}

/**
 * A parameter search around one backtest of the strategy's current version (ADR 0013): the market, period and
 * account as for a run, which parameters to search and in which ranges, and how the search runs. The optimization
 * freezes them when it starts.
 */
export function OptimizationSetupForm({
  strategy,
  onStarted,
}: {
  strategy: Strategy;
  onStarted: (job: BackgroundJob) => void;
}) {
  const [values, setValues] = useState<OptimizationSetupValues>(() =>
    optimizationSetupDefaults(strategy.definition),
  );
  const [errors, setErrors] = useState<OptimizationSetupErrors>({});
  const start = useMutation({ mutationFn: startOptimization, onSuccess: onStarted });
  const backtestErrors = errors.backtest ?? {};

  function set<K extends keyof OptimizationSetupValues>(key: K, value: OptimizationSetupValues[K]) {
    setValues((current) => ({ ...current, [key]: value }));
  }

  function setBacktest<K extends keyof OptimizationSetupValues['backtest']>(
    key: K,
    value: OptimizationSetupValues['backtest'][K],
  ) {
    setValues((current) => ({ ...current, backtest: { ...current.backtest, [key]: value } }));
  }

  function setRange(name: string, change: Partial<ParameterRange>) {
    setValues((current) => ({
      ...current,
      ranges: { ...current.ranges, [name]: { ...current.ranges[name]!, ...change } },
    }));
  }

  function backtestText(
    field:
      | 'from'
      | 'to'
      | 'startingCapital'
      | 'limitFeePercent'
      | 'marketFeePercent'
      | 'slippagePercent'
      | 'holdoutPercent',
    label: string,
  ) {
    const date = field === 'from' || field === 'to';

    return (
      <Checked label={label} error={backtestErrors[field]}>
        <Input
          inputMode={date ? undefined : 'decimal'}
          type={date ? 'date' : 'text'}
          value={values.backtest[field]}
          aria-invalid={backtestErrors[field] ? true : undefined}
          onChange={(event) => setBacktest(field, event.target.value)}
        />
      </Checked>
    );
  }

  function searchText(
    field: 'budget' | 'seed' | 'finalistCount' | 'minExecutions' | 'maxDrawdownPercent',
    label: string,
  ) {
    return (
      <Checked label={label} error={errors[field]}>
        <Input
          inputMode="decimal"
          value={values[field]}
          aria-invalid={errors[field] ? true : undefined}
          onChange={(event) => set(field, event.target.value)}
        />
      </Checked>
    );
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    const result = optimizationSetupRequest(
      strategy.id,
      strategy.currentVersion,
      strategy.definition,
      values,
    );

    if ('errors' in result) {
      setErrors(result.errors);

      return;
    }

    setErrors({});
    start.mutate(result.request);
  }

  return (
    <form className="space-y-6" aria-label="Настройка оптимизации" onSubmit={submit} noValidate>
      <Card title="Рынок и период">
        <div className="space-y-4">
          <SeriesFields
            value={values.backtest.series}
            onChange={(series) => setBacktest('series', series)}
            withKey
          />
          <div className="grid gap-4 sm:grid-cols-2">
            {backtestText('from', 'С (UTC), пусто — с начала истории')}
            {backtestText('to', 'По (UTC), пусто — до последней закрытой свечи')}
          </div>
        </div>
      </Card>
      <Card title="Счёт и исполнение">
        <div className="grid gap-4 sm:grid-cols-2">
          {backtestText('startingCapital', 'Начальный капитал, в валюте котировки')}
          {backtestText('slippagePercent', 'Проскальзывание рыночных ордеров, %')}
          {backtestText('limitFeePercent', 'Комиссия лимитных ордеров, %')}
          {backtestText('marketFeePercent', 'Комиссия рыночных ордеров, %')}
          {backtestText('holdoutPercent', 'Отложенная выборка в конце периода, %')}
        </div>
      </Card>
      <Card title="Параметры">
        {strategy.definition.parameters.length === 0 ? (
          <p className="text-muted">У стратегии нет параметров — искать нечего.</p>
        ) : (
          <div className="space-y-4">
            <p className="text-sm text-muted">
              Отмеченные параметры перебираются в диапазоне с шагом; остальные остаются со своим
              значением.
            </p>
            {strategy.definition.parameters.map((parameter) => {
              const range = values.ranges[parameter.name]!;
              const percent = isPercentageParameter(parameter.displayUnit) ? ', %' : '';
              const name = `${parameterReadableName(parameter.name)}${percent}`;
              const rangeError = errors[`ranges.${parameter.name}`];
              const fixedError = backtestErrors[`parameters.${parameter.name}`];

              return (
                <fieldset key={parameter.name} className="rounded-md border border-line p-3">
                  <legend className="px-1 text-sm">
                    <label className="inline-flex items-center gap-2">
                      <input
                        type="checkbox"
                        checked={range.enabled}
                        onChange={(event) =>
                          setRange(parameter.name, { enabled: event.target.checked })
                        }
                      />
                      Искать: {name}
                    </label>
                  </legend>
                  {range.enabled ? (
                    <div className="grid gap-4 sm:grid-cols-3">
                      {(['min', 'max', 'step'] as const).map((key) => (
                        <Field key={key} label={{ min: 'От', max: 'До', step: 'Шаг' }[key]}>
                          <Input
                            inputMode="decimal"
                            value={range[key]}
                            aria-invalid={rangeError ? true : undefined}
                            onChange={(event) =>
                              setRange(parameter.name, { [key]: event.target.value })
                            }
                          />
                        </Field>
                      ))}
                    </div>
                  ) : (
                    <Checked label="Значение" error={fixedError}>
                      <Input
                        inputMode="decimal"
                        value={values.backtest.parameters[parameter.name] ?? ''}
                        aria-invalid={fixedError ? true : undefined}
                        onChange={(event) =>
                          setBacktest('parameters', {
                            ...values.backtest.parameters,
                            [parameter.name]: event.target.value,
                          })
                        }
                      />
                    </Checked>
                  )}
                  {rangeError && (
                    <p role="alert" className="mt-1 text-sm text-danger-text">
                      {rangeError}
                    </p>
                  )}
                </fieldset>
              );
            })}
            {errors.ranges && (
              <p role="alert" className="text-sm text-danger-text">
                {errors.ranges}
              </p>
            )}
          </div>
        )}
      </Card>
      <Card title="Поиск">
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Метод">
            <select
              className={selectClassName}
              value={values.method}
              onChange={(event) => set('method', event.target.value as OptimizationMethod)}
            >
              {methods.map((method) => (
                <option key={method} value={method}>
                  {method === 'auto' ? 'Автоматически' : methodLabel(method)}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Цель на обучающей части">
            <select
              className={selectClassName}
              value={values.objective}
              onChange={(event) => set('objective', event.target.value as OptimizationObjective)}
            >
              {objectives.map((objective) => (
                <option key={objective} value={objective}>
                  {objectiveLabel(objective)}
                </option>
              ))}
            </select>
          </Field>
          {searchText(
            'budget',
            values.walkForward.enabled ? 'Испытаний на окно, не больше' : 'Испытаний, не больше',
          )}
          {searchText('seed', 'Зерно, пусто — случайное')}
          {searchText('finalistCount', 'Лучших испытаний прогнать полностью')}
          {searchText('minExecutions', 'Сделок не меньше')}
          {searchText('maxDrawdownPercent', 'Просадка не больше, %')}
        </div>
      </Card>
      <Card title="Проверка на скользящих окнах">
        <div className="space-y-4">
          <label className="inline-flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={values.walkForward.enabled}
              onChange={(event) =>
                setValues((current) => ({
                  ...current,
                  finalistCount: event.target.checked ? '1' : '3',
                  walkForward: { ...current.walkForward, enabled: event.target.checked },
                }))
              }
            />
            Искать на каждом обучающем окне и проверять победителя на следующем
          </label>
          {values.walkForward.enabled && (
            <div className="grid gap-4 sm:grid-cols-3">
              <Checked label="Окон" error={errors['walkForward.windows']}>
                <Input
                  inputMode="numeric"
                  value={values.walkForward.windows}
                  onChange={(event) =>
                    set('walkForward', { ...values.walkForward, windows: event.target.value })
                  }
                />
              </Checked>
              <Checked
                label="Обучение к проверке, раз"
                error={errors['walkForward.trainToTestRatio']}
              >
                <Input
                  inputMode="numeric"
                  value={values.walkForward.trainToTestRatio}
                  onChange={(event) =>
                    set('walkForward', {
                      ...values.walkForward,
                      trainToTestRatio: event.target.value,
                    })
                  }
                />
              </Checked>
              <Field label="Схема">
                <select
                  className={selectClassName}
                  value={values.walkForward.scheme}
                  onChange={(event) =>
                    set('walkForward', {
                      ...values.walkForward,
                      scheme: event.target.value as 'rolling' | 'anchored',
                    })
                  }
                >
                  <option value="rolling">Скользящее обучение</option>
                  <option value="anchored">Растущее обучение от начала</option>
                </select>
              </Field>
            </div>
          )}
        </div>
      </Card>
      {start.error && (
        <ApiErrorMessage error={start.error} fallback="Не удалось запустить оптимизацию." />
      )}
      <Button
        type="submit"
        variant="primary"
        disabled={start.isPending || values.backtest.series.venue === ''}
      >
        Запустить оптимизацию
      </Button>
    </form>
  );
}
