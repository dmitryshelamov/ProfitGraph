import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { type FormEvent, useState } from 'react';

import { credentialsQuery } from '@/modules/credentials';
import { acceptsCredential, venuesQuery } from '@/modules/market-data';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { Field } from '@/shared/ui/Field';
import { Input } from '@/shared/ui/Input';

import {
  scannerKeys,
  scanTradeSettingsQuery,
  scanVenuesQuery,
  setScanTradeSettings,
} from '../api/scannerApi';
import { withQuote } from '../model/format';
import { settingsProblem } from '../model/trades';
import type { ScanTradeSettings, ScanTradeSettingsInput } from '../model/types';
import { useScanQuote } from './useScanQuote';

const selectClass = 'w-full rounded-md border border-line bg-panel px-3 py-2 text-sm';

/**
 * How the trade ideas trade on the venue (ADR 0060): the owner's key the venue takes (on futures the Spot key, ADR 0061), the quote a
 * stop may lose and the most a trade may spend, and on futures the leverage of the trades' bots (ADR 0061). Shown as a summary once
 * set; «Изменить» opens the form again. Laid out for a narrow side column.
 */
export function ScanTradeSettingsForm({ venue }: { venue: string }) {
  const [editing, setEditing] = useState(false);
  const quote = useScanQuote(venue);
  const query = useQuery(scanTradeSettingsQuery(venue));
  const venues = useQuery(scanVenuesQuery());
  const credentials = useQuery(credentialsQuery());
  const margin = venues.data?.find((item) => item.venue === venue)?.supportsShort ?? false;
  const settings = query.data ?? null;
  const label = credentials.data?.find((item) => item.id === settings?.credentialId)?.label;

  if (!query.isSuccess) {
    return (
      <p className="text-sm text-muted">
        {query.isError ? 'Настройки не загрузились.' : 'Загрузка…'}
      </p>
    );
  }

  if (settings && !editing) {
    const rows = [
      { label: 'Ключ', value: label ?? '—' },
      { label: 'Риск на сделку', value: withQuote(settings.risk, quote) },
      { label: 'Не больше на сделку', value: withQuote(settings.maxAmount, quote) },
      ...(settings.leverage ? [{ label: 'Плечо', value: `${settings.leverage}×` }] : []),
    ];

    return (
      <div className="space-y-3">
        <dl aria-label="Настройки сделок" className="space-y-1.5 text-sm">
          {rows.map((row) => (
            <div key={row.label} className="flex justify-between gap-3">
              <dt className="text-text-secondary">{row.label}</dt>
              <dd className="text-right">{row.value}</dd>
            </div>
          ))}
        </dl>
        <Button size="sm" onClick={() => setEditing(true)}>
          Изменить
        </Button>
      </div>
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
      {!settings && (
        <p className="rounded-md border border-warning-border bg-warning-bg px-3 py-2 text-sm text-warning-text">
          Без риска и ключа идеи нельзя выбрать.
        </p>
      )}
      <div className="grid gap-3">
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
        Сумма сделки подбирается так, чтобы стоп с комиссиями терял риск, но не больше максимума.
        Ордера реальные: бот сделки торгует этим ключом.
        {margin &&
          ' На фьючерсах бот держит маржу — сумму, делённую на плечо. В one-way режиме Bybit лонг и шорт одной пары одновременно не встанут, в hedge — встанут.'}
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
