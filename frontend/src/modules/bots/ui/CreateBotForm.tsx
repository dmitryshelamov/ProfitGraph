import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { type FormEvent, useState } from 'react';

import { credentialsQuery } from '@/modules/credentials';
import { instrumentsQuery, venuesQuery, venuesReadWithKey } from '@/modules/market-data';
import { parameterReadableName, strategiesQuery, strategyQuery } from '@/modules/strategies';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { Field } from '@/shared/ui/Field';
import { Input } from '@/shared/ui/Input';

import { botKeys, createBot } from '../api/botsApi';
import {
  steamBotSettingsBody,
  type SteamBotSettingsDraft,
  steamBotSettingsDraft,
} from '../model/steamBotSettings';
import type { Bot } from '../model/types';

const timeframes = ['1m', '5m', '15m', '1h', '4h', '1d'];
const steamTimeframes = ['1h', '1d'];
const selectClass = 'rounded-md border border-line bg-panel px-3 py-2 text-sm';
const defaultSymbol = 'BTCUSDT';

/**
 * Creates a stopped bot of a saved strategy on a symbol of the chosen key's venue. Parameters start from the strategy's
 * defaults; decimals stay text until the server checks them. A Steam bot trades an item named exactly as Steam writes it
 * (<code>KZT:730:Dreams &amp; Nightmares Case</code>), on Steam's hourly or daily medians, with its own settings. A T-Invest
 * bot trades a MOEX share as <code>TQBR:SBER</code>, found with the chosen key. Timeframes are the venue's own.
 */
export function CreateBotForm({ onCreated }: { onCreated: (bot: Bot) => void }) {
  const queryClient = useQueryClient();
  const strategies = useQuery(strategiesQuery());
  const credentials = useQuery(credentialsQuery());
  const [strategyId, setStrategyId] = useState('');
  const [credentialId, setCredentialId] = useState('');
  const [symbol, setSymbol] = useState(defaultSymbol);
  const [timeframe, setTimeframe] = useState('1h');
  const [values, setValues] = useState<Record<string, string>>({});
  const [allocationCap, setAllocationCap] = useState('100');
  const [maxOrderQuote, setMaxOrderQuote] = useState('20');
  const [dailyLossLimitQuote, setDailyLossLimitQuote] = useState('10');
  const [allowForeignOrders, setAllowForeignOrders] = useState(false);
  const [steamSettings, setSteamSettings] = useState<SteamBotSettingsDraft | null>(null);
  const venues = useQuery(venuesQuery());
  const venue = credentials.data?.find((item) => item.id === credentialId)?.venue;
  const steam = venue === 'steam';
  const readWithKey = venue !== undefined && venuesReadWithKey.has(venue);
  const steamDefaults = venues.data?.find((item) => item.id === 'steam')?.botSettings;
  const settings = steamSettings ?? steamBotSettingsDraft(steamDefaults);
  const offered =
    venues.data?.find((item) => item.id === venue)?.timeframes ??
    (steam ? steamTimeframes : timeframes);
  const shares = useQuery({
    ...instrumentsQuery(venue ?? '', symbol.trim(), credentialId),
    enabled: readWithKey && credentialId !== '' && symbol.trim().length >= 2,
  });
  const chosenTimeframe = offered.includes(timeframe) ? timeframe : (offered[0] ?? '1h');
  const strategy = useQuery({ ...strategyQuery(strategyId), enabled: strategyId !== '' });
  const parameters = strategy.data?.definition.parameters ?? [];
  const create = useMutation({
    mutationFn: () =>
      createBot({
        strategyId,
        credentialId,
        symbol: steam ? symbol.trim() : symbol.trim().toUpperCase(),
        timeframe: chosenTimeframe,
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
        ...(steam ? { venueSettings: steamBotSettingsBody(settings) } : {}),
      }),
    onSuccess: async (bot) => {
      await queryClient.invalidateQueries({ queryKey: botKeys.all });
      onCreated(bot);
    },
  });

  function changeCredential(id: string) {
    const next = credentials.data?.find((item) => item.id === id)?.venue;
    setCredentialId(id);

    if (next !== undefined && next !== 'bybit' && symbol === defaultSymbol) {
      setSymbol('');
    }
  }

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
          onChange={(event) => changeCredential(event.target.value)}
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
          list={readWithKey ? 'bot-symbol-suggestions' : undefined}
          placeholder={
            readWithKey ? 'TQBR:SBER' : steam ? 'KZT:730:Dreams & Nightmares Case' : defaultSymbol
          }
          autoComplete="off"
          onChange={(event) => setSymbol(event.target.value)}
          required
          spellCheck={false}
        />
      </Field>
      {readWithKey && (
        <datalist id="bot-symbol-suggestions">
          {shares.data?.map((instrument) => (
            <option key={instrument.symbol} value={instrument.symbol}>
              {instrument.baseAsset}/{instrument.quoteAsset}
            </option>
          ))}
        </datalist>
      )}
      <Field label="Таймфрейм">
        <select
          className={selectClass}
          value={chosenTimeframe}
          onChange={(event) => setTimeframe(event.target.value)}
        >
          {offered.map((item) => (
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
      {steam && (
        <>
          <Field label="Холд после покупки, дней">
            <Input
              value={settings.settlementHoldDays}
              onChange={(event) =>
                setSteamSettings({ ...settings, settlementHoldDays: event.target.value })
              }
              inputMode="numeric"
              required
            />
          </Field>
          <Field label="Проскальзывание market buy">
            <Input
              value={settings.marketBuySlippage}
              onChange={(event) =>
                setSteamSettings({ ...settings, marketBuySlippage: event.target.value })
              }
              inputMode="decimal"
              required
            />
          </Field>
          <Field label="Срок market buy, секунд">
            <Input
              value={settings.marketBuyDeadlineSeconds}
              onChange={(event) =>
                setSteamSettings({ ...settings, marketBuyDeadlineSeconds: event.target.value })
              }
              inputMode="numeric"
              required
            />
          </Field>
          <p className="text-sm text-muted md:col-span-2">
            Холд — оценка для отображения: продаётся предмет после его собственной разблокировки.
            Проскальзывание — доля сверх лучшего лота, не больше 0.1.
          </p>
        </>
      )}
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
