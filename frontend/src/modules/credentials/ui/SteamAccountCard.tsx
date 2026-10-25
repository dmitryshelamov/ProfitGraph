import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import { useCurrentUser } from '@/modules/accounts';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { DateTime } from '@/shared/ui/DateTime';

import {
  accountProtectionQuery,
  accountStatusQuery,
  authenticatorCode,
  credentialKeys,
  resumeProtection,
} from '../api/credentialsApi';
import { protectionReasons, steamGuardStatuses } from '../model/steam';
import type { AuthenticatorCode, ExchangeCredential } from '../model/types';

/**
 * A Steam key's account: its Steam Guard state and next check, the account protection that holds its bots back with a
 * resume, and for the Owner the current Steam Guard code on request. The code is made on the server when asked, kept only
 * on screen and never cached.
 */
export function SteamAccountCard({ credential }: { credential: ExchangeCredential }) {
  const queryClient = useQueryClient();
  const user = useCurrentUser();
  const status = useQuery(accountStatusQuery(credential.id));
  const protection = useQuery(accountProtectionQuery(credential.id));
  const [code, setCode] = useState<AuthenticatorCode | null>(null);
  const resume = useMutation({
    mutationFn: () => resumeProtection(credential.id),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: credentialKeys.protection(credential.id) }),
  });
  const show = useMutation({
    mutationFn: () => authenticatorCode(credential.id),
    onSuccess: setCode,
  });

  return (
    <div className="space-y-3 text-sm">
      <p>
        <span className="font-medium">Steam Guard: </span>
        {status.data
          ? (steamGuardStatuses[status.data.status] ?? status.data.status)
          : status.isError
            ? 'неизвестно'
            : 'загрузка…'}
        {status.data?.nextCheckAt && (
          <span className="text-muted">
            {' '}
            Следующая проверка: <DateTime value={status.data.nextCheckAt} />.
          </span>
        )}
      </p>
      {protection.data?.open && (
        <div className="space-y-2" role="status">
          <p className="text-danger-text">
            Защита аккаунта остановила ботов:{' '}
            {protectionReasons[protection.data.reason ?? ''] ?? protection.data.reason}
            {protection.data.since && (
              <>
                {' '}
                (с <DateTime value={protection.data.since} />)
              </>
            )}
            .
          </p>
          {protection.data.automaticResumeAt && (
            <p className="text-muted">
              Возобновится сама <DateTime value={protection.data.automaticResumeAt} />.
            </p>
          )}
          <Button size="sm" disabled={resume.isPending} onClick={() => resume.mutate()}>
            Возобновить
          </Button>
          {resume.isError && (
            <ApiErrorMessage error={resume.error} fallback="Не удалось возобновить." />
          )}
        </div>
      )}
      {user?.role === 'Owner' && (
        <div className="space-y-2">
          <Button size="sm" disabled={show.isPending} onClick={() => show.mutate()}>
            Показать код Steam Guard
          </Button>
          {code && (
            <p>
              <span className="font-mono text-xl" aria-label="Код Steam Guard">
                {code.code}
              </span>{' '}
              <span className="text-muted">
                действует до <DateTime value={code.validUntil} />
              </span>
            </p>
          )}
          {show.isError && (
            <ApiErrorMessage error={show.error} fallback="Не удалось получить код." />
          )}
        </div>
      )}
    </div>
  );
}
