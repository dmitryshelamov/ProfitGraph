import { useMutation, useQueryClient } from '@tanstack/react-query';

import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { DateTime } from '@/shared/ui/DateTime';

import { credentialKeys, deleteCredential } from '../api/credentialsApi';
import type { ExchangeCredential } from '../model/types';

export function CredentialsTable({ credentials }: { credentials: readonly ExchangeCredential[] }) {
  const queryClient = useQueryClient();
  const remove = useMutation({
    mutationFn: deleteCredential,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: credentialKeys.all }),
  });

  return (
    <>
      <table className="w-full text-left text-sm">
        <thead className="text-text-secondary">
          <tr>
            <th className="py-2">Название</th>
            <th className="py-2">Биржа</th>
            <th className="py-2">Ключ</th>
            <th className="py-2">Добавлен</th>
            <th className="py-2" />
          </tr>
        </thead>
        <tbody>
          {credentials.map((credential) => (
            <tr key={credential.id} className="border-t border-line">
              <td className="py-2">{credential.label}</td>
              <td className="py-2">{credential.venue}</td>
              <td className="py-2 font-mono">…{credential.apiKeyLast4}</td>
              <td className="py-2">
                <DateTime value={credential.createdAt} />
              </td>
              <td className="py-2 text-right">
                <Button
                  size="sm"
                  variant="ghost"
                  disabled={remove.isPending}
                  onClick={() => remove.mutate(credential.id)}
                >
                  Удалить
                </Button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {remove.isError && (
        <div className="mt-3">
          <ApiErrorMessage error={remove.error} fallback="Не удалось удалить ключ." />
        </div>
      )}
    </>
  );
}
