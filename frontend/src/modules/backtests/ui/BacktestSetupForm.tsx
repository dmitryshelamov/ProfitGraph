import { useMutation, useQuery } from '@tanstack/react-query';
import { type FormEvent, type ReactElement, type ReactNode, useState } from 'react';

import type { BackgroundJob } from '@/modules/jobs';
import { SeriesFields, type SeriesSelection, venuesQuery } from '@/modules/market-data';
import { isPercentageParameter, parameterReadableName, type Strategy } from '@/modules/strategies';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { Card } from '@/shared/ui/Card';
import { Field } from '@/shared/ui/Field';
import { Input } from '@/shared/ui/Input';

import { startBacktest } from '../api/backtestsApi';
import {
  backtestSetupDefaults,
  type BacktestSetupErrors,
  type BacktestSetupField,
  backtestSetupRequest,
  type BacktestSetupValues,
  withVenueDefaults,
} from '../model/backtestSetup';

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
 * Settings of one run of the strategy's current version: market and period, account, holdout and parameter values.
 * The run freezes them when it starts; later edits of the strategy do not change it.
 */
export function BacktestSetupForm({
  strategy,
  onStarted,
}: {
  strategy: Strategy;
  onStarted: (job: BackgroundJob) => void;
}) {
  const [values, setValues] = useState<BacktestSetupValues>(() =>
    backtestSetupDefaults(strategy.definition),
  );
  const [errors, setErrors] = useState<BacktestSetupErrors>({});
  const start = useMutation({ mutationFn: startBacktest, onSuccess: onStarted });
  const venues = useQuery(venuesQuery());

  function set<K extends keyof BacktestSetupValues>(key: K, value: BacktestSetupValues[K]) {
    setValues((current) => ({ ...current, [key]: value }));
  }

  /** A new venue brings its own fees and hold; the rest of the form stays. */
  function setSeries(series: SeriesSelection) {
    setValues((current) =>
      current.series.venue === series.venue
        ? { ...current, series }
        : withVenueDefaults(
            { ...current, series },
            venues.data?.find((venue) => venue.id === series.venue),
          ),
    );
  }

  function sellerFeeField(index: number, label: string) {
    const field = `sellerFeePercents.${index}` as const;
    const fees = values.sellerFees!;

    return (
      <Checked key={field} label={label} error={errors[field]}>
        <Input
          inputMode="decimal"
          value={fees.percents[index] ?? ''}
          aria-invalid={errors[field] ? true : undefined}
          onChange={(event) =>
            set('sellerFees', {
              ...fees,
              percents: fees.percents.map((percent, position) =>
                position === index ? event.target.value : percent,
              ),
            })
          }
        />
      </Checked>
    );
  }

  function text(
    field: Exclude<
      BacktestSetupField,
      `parameters.${string}` | `sellerFeePercents.${number}` | 'sellerFeeMinimum' | 'holdDays'
    >,
    label: ReactNode,
    inputMode: 'decimal' | undefined = 'decimal',
  ) {
    return (
      <Checked label={label} error={errors[field]}>
        <Input
          inputMode={inputMode}
          type={field === 'from' || field === 'to' ? 'date' : 'text'}
          value={values[field]}
          aria-invalid={errors[field] ? true : undefined}
          onChange={(event) => set(field, event.target.value)}
        />
      </Checked>
    );
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    const result = backtestSetupRequest(
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
    <form className="space-y-6" aria-label="Настройка прогона" onSubmit={submit} noValidate>
      <Card title="Рынок и период">
        <div className="space-y-4">
          <SeriesFields value={values.series} onChange={setSeries} />
          <div className="grid gap-4 sm:grid-cols-2">
            {text('from', 'С (UTC), пусто — с начала истории', undefined)}
            {text('to', 'По (UTC), пусто — до последней закрытой свечи', undefined)}
          </div>
        </div>
      </Card>
      <Card title="Счёт и исполнение">
        <div className="grid gap-4 sm:grid-cols-2">
          {text('startingCapital', 'Начальный капитал, в валюте котировки')}
          {text('slippagePercent', 'Проскальзывание рыночных ордеров, %')}
          {values.sellerFees ? (
            <>
              {values.sellerFees.percents.map((_, index) =>
                sellerFeeField(index, `Комиссия продавца, часть ${index + 1}, % с единицы`),
              )}
              <Checked label="Минимум каждой части комиссии" error={errors.sellerFeeMinimum}>
                <Input
                  inputMode="decimal"
                  value={values.sellerFees.minimum}
                  aria-invalid={errors.sellerFeeMinimum ? true : undefined}
                  onChange={(event) =>
                    set('sellerFees', { ...values.sellerFees!, minimum: event.target.value })
                  }
                />
              </Checked>
            </>
          ) : (
            <>
              {text('limitFeePercent', 'Комиссия лимитных ордеров, %')}
              {text('marketFeePercent', 'Комиссия рыночных ордеров, %')}
            </>
          )}
          {values.holdDays === null ? null : (
            <Checked label="Продажа купленного не раньше чем через, дней" error={errors.holdDays}>
              <Input
                inputMode="numeric"
                value={values.holdDays}
                aria-invalid={errors.holdDays ? true : undefined}
                onChange={(event) => set('holdDays', event.target.value)}
              />
            </Checked>
          )}
          {text('holdoutPercent', 'Отложенная выборка в конце периода, %')}
        </div>
      </Card>
      <Card title="Параметры стратегии">
        {strategy.definition.parameters.length === 0 ? (
          <p className="text-muted">У стратегии нет параметров.</p>
        ) : (
          <div className="grid gap-4 sm:grid-cols-2">
            {strategy.definition.parameters.map((parameter) => {
              const field = `parameters.${parameter.name}` as const;

              return (
                <Checked
                  key={parameter.name}
                  label={`${parameterReadableName(parameter.name)}${isPercentageParameter(parameter.displayUnit) ? ', %' : ''}`}
                  error={errors[field]}
                >
                  <Input
                    inputMode="decimal"
                    value={values.parameters[parameter.name] ?? ''}
                    aria-invalid={errors[field] ? true : undefined}
                    onChange={(event) =>
                      set('parameters', {
                        ...values.parameters,
                        [parameter.name]: event.target.value,
                      })
                    }
                  />
                </Checked>
              );
            })}
          </div>
        )}
      </Card>
      {start.error && (
        <ApiErrorMessage error={start.error} fallback="Не удалось запустить прогон." />
      )}
      <Button
        type="submit"
        variant="primary"
        disabled={start.isPending || values.series.venue === ''}
      >
        Запустить прогон
      </Button>
    </form>
  );
}
