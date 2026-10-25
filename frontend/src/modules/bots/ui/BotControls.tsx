import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';

import { botKeys, killBot, startBot, stopBot } from '../api/botsApi';
import type { Bot } from '../model/types';

/**
 * Start and stop change what the bot should do; the Worker follows. The kill switch asks to confirm: it rejects unsent
 * orders and cancels open ones without selling the position.
 */
export function BotControls({ bot }: { bot: Bot }) {
  const queryClient = useQueryClient();
  const [confirmingKill, setConfirmingKill] = useState(false);
  const change = useMutation({
    mutationFn: (action: (id: string) => Promise<Bot>) => action(bot.id),
    onSuccess: (updated) => {
      queryClient.setQueryData(botKeys.detail(bot.id), updated);
      setConfirmingKill(false);

      return queryClient.invalidateQueries({ queryKey: botKeys.all });
    },
  });
  const running = bot.desiredState === 'Running';

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap gap-2">
        {running ? (
          <Button disabled={change.isPending} onClick={() => change.mutate(stopBot)}>
            Остановить
          </Button>
        ) : (
          <Button
            variant="primary"
            disabled={change.isPending}
            onClick={() => change.mutate(startBot)}
          >
            Запустить
          </Button>
        )}
        {running && bot.runtimeStatus === 'Degraded' && (
          <Button disabled={change.isPending} onClick={() => change.mutate(startBot)}>
            Перезапустить
          </Button>
        )}
        {confirmingKill ? (
          <>
            <Button disabled={change.isPending} onClick={() => change.mutate(killBot)}>
              Подтвердить аварийную остановку
            </Button>
            <Button variant="ghost" onClick={() => setConfirmingKill(false)}>
              Отмена
            </Button>
          </>
        ) : (
          <Button variant="ghost" onClick={() => setConfirmingKill(true)}>
            Аварийная остановка
          </Button>
        )}
      </div>
      {confirmingKill && (
        <p className="text-sm text-muted">
          Неотправленные заявки будут отклонены, открытые — отменены; позиция не продаётся.
        </p>
      )}
      {change.isError && (
        <ApiErrorMessage error={change.error} fallback="Не удалось изменить состояние бота." />
      )}
    </div>
  );
}
