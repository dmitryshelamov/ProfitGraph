import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { type FormEvent, useState } from 'react';

import { credentialsQuery } from '@/modules/credentials';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { Field } from '@/shared/ui/Field';
import { Input } from '@/shared/ui/Input';

import { scannerKeys, setScanTradeSettings } from '../api/scannerApi';
import { settingsProblem } from '../model/trades';
import type { ScanTradeSettings, ScanTradeSettingsInput } from '../model/types';

const selectClass = 'rounded-md border border-line bg-panel px-3 py-2 text-sm';

/**
 * How the trade cards trade on the venue (ADR 0060): the owner's key of that venue, the quote a stop may lose and the most a trade may
 * spend. Shown as a summary once set; «Изменить» opens the form again.
 */
export function ScanTradeSettingsForm({
  venue,
  settings,
}: {
  venue: string;
  settings: ScanTradeSettings | null;
}) {
  const [editing, setEditing] = useState(false);
  const credentials = useQuery(credentialsQuery());
  const label = credentials.data?.find((item) => item.id === settings?.credentialId)?.label;

  if (settings && !editing) {
    return (
      <p className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-text-secondary">
        <span>
          Риск на сделку <span className="text-text">{settings.risk} USDT</span>, не больше{' '}
          <span className="text-text">{settings.maxAmount} USDT</span> на сделку
          {label && <>, ключ «{label}»</>}
        </span>
        <Button size="sm" variant="ghost" onClick={() => setEditing(true)}>
          Изменить
        </Button>
      </p>
    );
  }

  return <SettingsEditor venue={venue} settings={settings} onDone={() => setEditing(false)} />;
}

function SettingsEditor({
  venue,
  settings,
  onDone,
}: {
  venue: string;
  settings: ScanTradeSettings | null;
  onDone: () => void;
}) {
  const queryClient = useQueryClient();
  const credentials = useQuery(credentialsQuery());
  const own = credentials.data?.filter((item) => item.venue === venue) ?? [];
  const [credentialId, setCredentialId] = useState(settings?.credentialId ?? '');
  const [risk, setRisk] = useState(settings?.risk ?? '');
  const [maxAmount, setMaxAmount] = useState(settings?.maxAmount ?? '');
  const save = useMutation({
    mutationFn: (input: ScanTradeSettingsInput) => setScanTradeSettings(venue, input),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: scannerKeys.tradeSettings(venue) });
      await queryClient.invalidateQueries({ queryKey: ['scanner', venue, 'trade-ideas'] });
      onDone();
    },
  });
  const chosen = credentialId || (own[0]?.id ?? '');
  const problem = settingsProblem(chosen, risk, maxAmount);

  function submit(event: FormEvent) {
    event.preventDefault();

    if (!problem) {
      save.mutate({ credentialId: chosen, risk: risk.trim(), maxAmount: maxAmount.trim() });
    }
  }

  return (
    <form onSubmit={submit} className="space-y-3" aria-label="Настройки сделок">
      <div className="grid gap-3 sm:grid-cols-3">
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
        <Field label="Риск на сделку, USDT">
          <Input
            inputMode="decimal"
            value={risk}
            onChange={(event) => setRisk(event.target.value)}
          />
        </Field>
        <Field label="Не больше на сделку, USDT">
          <Input
            inputMode="decimal"
            value={maxAmount}
            onChange={(event) => setMaxAmount(event.target.value)}
          />
        </Field>
      </div>
      <p className="text-xs text-muted">
        Сумма сделки подбирается так, чтобы стоп с комиссиями терял риск, но не больше указанного
        максимума. Ордера реальные: бот сделки торгует ключом выше.
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
