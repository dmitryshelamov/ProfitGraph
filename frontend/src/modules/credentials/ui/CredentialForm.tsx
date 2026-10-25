import { useMutation, useQueryClient } from '@tanstack/react-query';
import { type FormEvent, useState } from 'react';

import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { Field } from '@/shared/ui/Field';
import { Input } from '@/shared/ui/Input';

import { createCredential, credentialKeys } from '../api/credentialsApi';

/**
 * Adds an exchange key. The secret is sent once and never shown again; the key needs Spot trading without withdrawals.
 */
export function CredentialForm({ onCreated }: { onCreated?: () => void }) {
  const queryClient = useQueryClient();
  const [label, setLabel] = useState('');
  const [apiKey, setApiKey] = useState('');
  const [apiSecret, setApiSecret] = useState('');
  const create = useMutation({
    mutationFn: () =>
      createCredential({
        venue: 'bybit',
        label: label.trim(),
        apiKey: apiKey.trim(),
        apiSecret: apiSecret.trim(),
      }),
    onSuccess: async () => {
      setApiKey('');
      setApiSecret('');
      setLabel('');
      await queryClient.invalidateQueries({ queryKey: credentialKeys.all });
      onCreated?.();
    },
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    create.mutate();
  }

  return (
    <form onSubmit={submit} className="grid gap-4 md:grid-cols-2" aria-label="Новый ключ биржи">
      <Field label="Биржа">
        <Input value="Bybit (Spot)" readOnly />
      </Field>
      <Field label="Название ключа">
        <Input
          value={label}
          onChange={(event) => setLabel(event.target.value)}
          required
          maxLength={64}
        />
      </Field>
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
      <div className="flex items-center gap-3 md:col-span-2">
        <Button type="submit" variant="primary" disabled={create.isPending}>
          Сохранить ключ
        </Button>
        <p className="text-sm text-muted">
          Ключ хранится зашифрованным; нужен Spot-трейдинг без права вывода.
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
