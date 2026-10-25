import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { DateTime } from '@/shared/ui/DateTime';

import {
  createTelegramLinkCode,
  telegramKeys,
  telegramLinkQuery,
  unlinkTelegram,
} from '../api/telegramApi';

/**
 * Bot alerts in Telegram: the user sends `/start` with a one-time code to the bot; alerts raised after that arrive in
 * the chat until it is unlinked or the bot is blocked.
 */
export function TelegramLinkCard() {
  const queryClient = useQueryClient();
  const link = useQuery(telegramLinkQuery());
  const code = useMutation({ mutationFn: createTelegramLinkCode });
  const unlink = useMutation({
    mutationFn: unlinkTelegram,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: telegramKeys.link }),
  });

  if (link.isError) {
    return (
      <p role="alert" className="text-danger-text">
        Не удалось загрузить настройки Telegram.
      </p>
    );
  }

  if (!link.data) {
    return <p className="text-muted">Загрузка…</p>;
  }

  const status = link.data;

  return (
    <div className="space-y-3 text-sm">
      {!status.enabled && (
        <p className="text-muted">Telegram-уведомления в этой установке выключены.</p>
      )}
      {status.linked ? (
        <p>
          Чат привязан
          {status.linkedAt && (
            <>
              {' '}
              (<DateTime value={status.linkedAt} />)
            </>
          )}
          {status.blocked && ', но бот заблокирован в Telegram — алерты не приходят'}.
        </p>
      ) : (
        <p className="text-muted">Чат не привязан.</p>
      )}
      <div className="flex flex-wrap gap-2">
        <Button size="sm" disabled={code.isPending} onClick={() => code.mutate()}>
          Получить код привязки
        </Button>
        {status.linked && (
          <Button
            size="sm"
            variant="ghost"
            disabled={unlink.isPending}
            onClick={() => unlink.mutate()}
          >
            Отвязать чат
          </Button>
        )}
      </div>
      {code.data && (
        <p role="status">
          Отправьте боту{status.botUsername && ` @${status.botUsername}`}:{' '}
          <span className="font-mono">/start {code.data.code}</span> до{' '}
          <DateTime value={code.data.expiresAt} />
          {code.data.startLink && (
            <>
              {' '}
              или откройте{' '}
              <a
                href={code.data.startLink}
                className="text-link underline"
                target="_blank"
                rel="noreferrer"
              >
                ссылку
              </a>
            </>
          )}
          .
        </p>
      )}
      {code.isError && <ApiErrorMessage error={code.error} fallback="Не удалось получить код." />}
      {unlink.isError && (
        <ApiErrorMessage error={unlink.error} fallback="Не удалось отвязать чат." />
      )}
    </div>
  );
}
