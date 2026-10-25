import { useMutation, useQueryClient } from '@tanstack/react-query';
import { type FormEvent, useState } from 'react';

import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { Field } from '@/shared/ui/Field';
import { Input } from '@/shared/ui/Input';

import { createCredential, credentialKeys, listVenueAccounts } from '../api/credentialsApi';
import type { VenueAccount } from '../model/types';

const selectClassName =
  'w-full rounded-md border border-line-strong bg-panel px-3 py-2 text-text outline-none focus:border-link';

const ineligibility: Record<string, string> = {
  not_open: 'счёт не открыт',
  not_full_access: 'у токена нет полного доступа к счёту',
  unsupported_kind: 'бот не торгует на счёте этого вида',
};

/**
 * Adds an exchange key. A Bybit key is an API key and secret with Spot trading and without withdrawals. A T-Invest key is
 * a token and the brokerage account it trades: the token loads the accounts, one eligible account is chosen, and saving
 * checks it again. Secrets are sent once, never cached and never shown again.
 */
export function CredentialForm({ onCreated }: { onCreated?: () => void }) {
  const queryClient = useQueryClient();
  const [venue, setVenue] = useState<'bybit' | 'tinvest'>('bybit');
  const [label, setLabel] = useState('');
  const [apiKey, setApiKey] = useState('');
  const [apiSecret, setApiSecret] = useState('');
  const [token, setToken] = useState('');
  const [accounts, setAccounts] = useState<VenueAccount[] | null>(null);
  const [accountId, setAccountId] = useState('');
  const load = useMutation({
    mutationFn: () => listVenueAccounts('tinvest', { token: token.trim() }),
    onSuccess: (loaded) => {
      setAccounts(loaded);
      setAccountId(loaded.find((account) => account.eligible)?.id ?? '');
    },
  });
  const create = useMutation({
    mutationFn: () =>
      venue === 'tinvest'
        ? createCredential({
            venue,
            label: label.trim(),
            fields: { token: token.trim(), accountId },
          })
        : createCredential({
            venue,
            label: label.trim(),
            apiKey: apiKey.trim(),
            apiSecret: apiSecret.trim(),
          }),
    onSuccess: async () => {
      setApiKey('');
      setApiSecret('');
      setToken('');
      setAccounts(null);
      setAccountId('');
      setLabel('');
      await queryClient.invalidateQueries({ queryKey: credentialKeys.all });
      onCreated?.();
    },
  });

  function changeToken(value: string) {
    setToken(value);
    setAccounts(null);
    setAccountId('');
    load.reset();
  }

  function changeVenue(value: string) {
    setVenue(value === 'tinvest' ? 'tinvest' : 'bybit');
    changeToken('');
    create.reset();
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    create.mutate();
  }

  return (
    <form onSubmit={submit} className="grid gap-4 md:grid-cols-2" aria-label="Новый ключ биржи">
      <Field label="Биржа">
        <select
          className={selectClassName}
          value={venue}
          onChange={(event) => changeVenue(event.target.value)}
        >
          <option value="bybit">Bybit (Spot)</option>
          <option value="tinvest">T-Invest (MOEX)</option>
        </select>
      </Field>
      <Field label="Название ключа">
        <Input
          value={label}
          onChange={(event) => setLabel(event.target.value)}
          required
          maxLength={64}
        />
      </Field>
      {venue === 'bybit' ? (
        <>
          <Field label="API key">
            <Input
              value={apiKey}
              onChange={(event) => setApiKey(event.target.value)}
              required
              autoComplete="off"
              spellCheck={false}
            />
          </Field>
          <Field label="API secret">
            <Input
              type="password"
              value={apiSecret}
              onChange={(event) => setApiSecret(event.target.value)}
              required
              autoComplete="new-password"
              spellCheck={false}
            />
          </Field>
        </>
      ) : (
        <>
          <Field label="Токен T-Invest">
            <Input
              type="password"
              value={token}
              onChange={(event) => changeToken(event.target.value)}
              required
              autoComplete="new-password"
              spellCheck={false}
            />
          </Field>
          <div className="flex items-end">
            <Button
              type="button"
              disabled={token.trim() === '' || load.isPending}
              onClick={() => load.mutate()}
            >
              Загрузить счета
            </Button>
          </div>
          {accounts && (
            <fieldset className="space-y-2 md:col-span-2">
              <legend className="text-sm font-medium">Счёт для торговли</legend>
              {accounts.length === 0 && (
                <p className="text-sm text-muted">Токен не видит ни одного счёта.</p>
              )}
              {accounts.map((account) => (
                <label key={account.id} className="flex items-center gap-2 text-sm">
                  <input
                    type="radio"
                    name="tinvest-account"
                    value={account.id}
                    checked={accountId === account.id}
                    disabled={!account.eligible}
                    onChange={() => setAccountId(account.id)}
                  />
                  <span>
                    {account.name} (…{account.id.slice(-4)})
                    {!account.eligible && (
                      <span className="text-muted">
                        {' — '}
                        {ineligibility[account.ineligibility ?? ''] ?? 'недоступен для бота'}
                      </span>
                    )}
                  </span>
                </label>
              ))}
            </fieldset>
          )}
          {load.isError && (
            <div className="md:col-span-2">
              <ApiErrorMessage error={load.error} fallback="Не удалось загрузить счета." />
            </div>
          )}
        </>
      )}
      <div className="flex items-center gap-3 md:col-span-2">
        <Button
          type="submit"
          variant="primary"
          disabled={create.isPending || (venue === 'tinvest' && accountId === '')}
        >
          Сохранить ключ
        </Button>
        <p className="text-sm text-muted">
          {venue === 'tinvest'
            ? 'Токен хранится зашифрованным; нужен полный доступ к выбранному счёту.'
            : 'Ключ хранится зашифрованным; нужен Spot-трейдинг без права вывода.'}
        </p>
      </div>
      {create.isError && (
        <div className="md:col-span-2">
          <ApiErrorMessage error={create.error} fallback="Не удалось сохранить ключ." />
        </div>
      )}
    </form>
  );
}
