import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { type FormEvent, useState } from 'react';

import { credentialsQuery } from '@/modules/credentials';
import { parameterReadableName, strategiesQuery, strategyQuery } from '@/modules/strategies';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { Field } from '@/shared/ui/Field';
import { Input } from '@/shared/ui/Input';

import { botKeys, createBot } from '../api/botsApi';
import type { Bot } from '../model/types';

const timeframes = ['1m', '5m', '15m', '1h', '4h', '1d'];
const selectClass = 'rounded-md border border-line bg-panel px-3 py-2 text-sm';

/**
 * Creates a stopped bot of a saved strategy on a Bybit Spot symbol with one of the user's keys. Parameters start from
 * the strategy's defaults; decimals stay text until the server checks them.
 */
export function CreateBotForm({ onCreated }: { onCreated: (bot: Bot) => void }) {
  const queryClient = useQueryClient();
  const strategies = useQuery(strategiesQuery());
  const credentials = useQuery(credentialsQuery());
  const [strategyId, setStrategyId] = useState('');
  const [credentialId, setCredentialId] = useState('');
  const [symbol, setSymbol] = useState('BTCUSDT');
  const [timeframe, setTimeframe] = useState('1h');
  const [values, setValues] = useState<Record<string, string>>({});
  const [allocationCap, setAllocationCap] = useState('100');
  const [maxOrderQuote, setMaxOrderQuote] = useState('20');
  const [dailyLossLimitQuote, setDailyLossLimitQuote] = useState('10');
  const [allowForeignOrders, setAllowForeignOrders] = useState(false);
  const strategy = useQuery({ ...strategyQuery(strategyId), enabled: strategyId !== '' });
  const parameters = strategy.data?.definition.parameters ?? [];
  const create = useMutation({
    mutationFn: () =>
      createBot({
        strategyId,
        credentialId,
        symbol: symbol.trim().toUpperCase(),
        timeframe,
        parameters: Object.fromEntries(
          parameters.map((parameter) => [
            parameter.name,
            (values[parameter.name] ?? parameter.defaultValue).trim(),
          ]),
        ),
        allocationCap: allocationCap.trim(),
        maxOrderQuote: maxOrderQuote.trim(),
        dailyLossLimitQuote: dailyLossLimitQuote.trim(),
        allowForeignOrders,
      }),
    onSuccess: async (bot) => {
      await queryClient.invalidateQueries({ queryKey: botKeys.all });
      onCreated(bot);
    },
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    create.mutate();
  }

  return (
    <form onSubmit={submit} className="grid gap-4 md:grid-cols-2" aria-label="Новый бот">
      <Field label="Стратегия">
        <select
          className={selectClass}
          value={strategyId}
          onChange={(event) => setStrategyId(event.target.value)}
          required
        >
          <option value="">Выберите стратегию</option>
          {strategies.data?.map((item) => (
            <option key={item.id} value={item.id}>
              {item.name}
            </option>
          ))}
        </select>
      </Field>
      <Field label="Ключ биржи">
        <select
          className={selectClass}
          value={credentialId}
          onChange={(event) => setCredentialId(event.target.value)}
          required
        >
          <option value="">Выберите ключ</option>
          {credentials.data?.map((item) => (
            <option key={item.id} value={item.id}>
              {item.label} (…{item.apiKeyLast4})
            </option>
          ))}
        </select>
      </Field>
      <Field label="Символ">
        <Input
          value={symbol}
          onChange={(event) => setSymbol(event.target.value)}
          required
          spellCheck={false}
        />
      </Field>
      <Field label="Таймфрейм">
        <select
          className={selectClass}
          value={timeframe}
          onChange={(event) => setTimeframe(event.target.value)}
        >
          {timeframes.map((item) => (
            <option key={item} value={item}>
              {item}
            </option>
          ))}
        </select>
      </Field>
      {parameters.map((parameter) => (
        <Field key={parameter.name} label={parameterReadableName(parameter.name)}>
          <Input
            value={values[parameter.name] ?? parameter.defaultValue}
            onChange={(event) =>
              setValues((current) => ({ ...current, [parameter.name]: event.target.value }))
            }
            inputMode="decimal"
            spellCheck={false}
          />
        </Field>
      ))}
      <Field label="Капитал бота">
        <Input
          value={allocationCap}
          onChange={(event) => setAllocationCap(event.target.value)}
          inputMode="decimal"
          required
        />
      </Field>
      <Field label="Лимит на заявку">
        <Input
          value={maxOrderQuote}
          onChange={(event) => setMaxOrderQuote(event.target.value)}
          inputMode="decimal"
          required
        />
      </Field>
      <Field label="Дневной лимит убытка">
        <Input
          value={dailyLossLimitQuote}
          onChange={(event) => setDailyLossLimitQuote(event.target.value)}
          inputMode="decimal"
          required
        />
      </Field>
      <label className="flex items-center gap-2 self-end text-sm">
        <input
          type="checkbox"
          checked={allowForeignOrders}
          onChange={(event) => setAllowForeignOrders(event.target.checked)}
        />
        Разрешить чужие заявки на символе
      </label>
      <div className="md:col-span-2">
        <Button type="submit" variant="primary" disabled={create.isPending}>
          Создать бота
        </Button>
      </div>
      {create.isError && (
        <div className="md:col-span-2">
          <ApiErrorMessage error={create.error} fallback="Не удалось создать бота." />
        </div>
      )}
    </form>
  );
}
