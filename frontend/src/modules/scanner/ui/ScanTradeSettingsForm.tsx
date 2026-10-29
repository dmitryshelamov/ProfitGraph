import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { type FormEvent, useState } from 'react';

import { credentialsQuery } from '@/modules/credentials';
import { acceptsCredential, venuesQuery } from '@/modules/market-data';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { Field } from '@/shared/ui/Field';
import { Input } from '@/shared/ui/Input';

import { scannerKeys, setScanTradeSettings } from '../api/scannerApi';
import { withQuote } from '../model/format';
import { settingsProblem } from '../model/trades';
import type { ScanTradeSettings, ScanTradeSettingsInput } from '../model/types';
import { useScanQuote } from './useScanQuote';

const selectClass = 'rounded-md border border-line bg-panel px-3 py-2 text-sm';

/**
 * How the trade cards trade on the venue (ADR 0060): the owner's key the venue takes (on futures the Spot key, ADR 0061), the quote a
 * stop may lose and the most a trade may spend, and on futures the leverage of the trades' bots (ADR 0061). Shown as a summary once set; «Изменить» opens the form again.
 */
export function ScanTradeSettingsForm({
  venue,
  settings,
  margin = false,
}: {
  venue: string;
  settings: ScanTradeSettings | null;
  margin?: boolean;
}) {
  const [editing, setEditing] = useState(false);
  const quote = useScanQuote(venue);
  const credentials = useQuery(credentialsQuery());
  const label = credentials.data?.find((item) => item.id === settings?.credentialId)?.label;

  if (settings && !editing) {
    return (
      <p className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-text-secondary">
        <span>
          Риск на сделку <span className="text-text">{withQuote(settings.risk, quote)}</span>, не
          больше <span className="text-text">{withQuote(settings.maxAmount, quote)}</span> на сделку
          {settings.leverage && <>, плечо {settings.leverage}×</>}
          {label && <>, ключ «{label}»</>}
        </span>
        <Button size="sm" variant="ghost" onClick={() => setEditing(true)}>
          Изменить
        </Button>
      </p>
    );
  }

  return (
    <SettingsEditor
      venue={venue}
      settings={settings}
      margin={margin}
      onDone={() => setEditing(false)}
    />
  );
}

function SettingsEditor({
  venue,
  settings,
  margin,
  onDone,
}: {
  venue: string;
  settings: ScanTradeSettings | null;
  margin: boolean;
  onDone: () => void;
}) {
  const queryClient = useQueryClient();
  const quote = useScanQuote(venue);
  const credentials = useQuery(credentialsQuery());
  const venues = useQuery(venuesQuery());
  const own =
    credentials.data?.filter((item) => acceptsCredential(venues.data, venue, item.venue)) ?? [];
  const [credentialId, setCredentialId] = useState(settings?.credentialId ?? '');
  const [risk, setRisk] = useState(settings?.risk ?? '');
  const [maxAmount, setMaxAmount] = useState(settings?.maxAmount ?? '');
  const [leverage, setLeverage] = useState(settings?.leverage ?? '1');
  const save = useMutation({
    mutationFn: (input: ScanTradeSettingsInput) => setScanTradeSettings(venue, input),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: scannerKeys.tradeSettings(venue) });
      await queryClient.invalidateQueries({ queryKey: ['scanner', venue, 'trade-ideas'] });
      onDone();
    },
  });
  const chosen = credentialId || (own[0]?.id ?? '');
  const problem = settingsProblem(chosen, risk, maxAmount, margin ? leverage : null);

  function submit(event: FormEvent) {
    event.preventDefault();

    if (!problem) {
      save.mutate({
        credentialId: chosen,
        risk: risk.trim(),
        maxAmount: maxAmount.trim(),
        ...(margin ? { leverage: leverage.trim() } : {}),
      });
    }
  }

  return (
    <form onSubmit={submit} className="space-y-3" aria-label="Настройки сделок">
      <div className={`grid gap-3 ${margin ? 'sm:grid-cols-4' : 'sm:grid-cols-3'}`}>
        <Field label="Ключ биржи">
          <select
            className={selectClass}
            value={chosen}
            onChange={(event) => setCredentialId(event.target.value)}
          >
            {own.length === 0 && <option value="">Нет ключей этой биржи</option>}
            {own.map((item) => (
              <option key={item.id} value={item.id}>
                {item.label}
              </option>
            ))}
          </select>
        </Field>
        <Field label={withQuote('Риск на сделку,', quote, 'Риск на сделку')}>
          <Input
            inputMode="decimal"
            value={risk}
            onChange={(event) => setRisk(event.target.value)}
          />
        </Field>
        <Field label={withQuote('Не больше на сделку,', quote, 'Не больше на сделку')}>
          <Input
            inputMode="decimal"
            value={maxAmount}
            onChange={(event) => setMaxAmount(event.target.value)}
          />
        </Field>
        {margin && (
          <Field label="Плечо">
            <Input
              inputMode="decimal"
              value={leverage}
              onChange={(event) => setLeverage(event.target.value)}
            />
          </Field>
        )}
      </div>
      <p className="text-xs text-muted">
        Сумма сделки подбирается так, чтобы стоп с комиссиями терял риск, но не больше указанного
        максимума. Ордера реальные: бот сделки торгует ключом выше.
        {margin &&
          ' На фьючерсах бот держит маржу — сумму, делённую на плечо; нужен hedge mode позиций на Bybit.'}
      </p>
      <div className="flex flex-wrap items-center gap-3">
        <Button type="submit" size="sm" disabled={save.isPending || problem !== null}>
          Сохранить
        </Button>
        {settings && (
          <Button size="sm" variant="ghost" onClick={onDone}>
            Отмена
          </Button>
        )}
        {problem && (risk || maxAmount) && <span className="text-sm text-muted">{problem}</span>}
      </div>
      {save.isError && (
        <ApiErrorMessage error={save.error} fallback="Не удалось сохранить настройки." />
      )}
    </form>
  );
}
