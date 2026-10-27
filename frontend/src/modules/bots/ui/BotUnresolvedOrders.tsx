import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { Card } from '@/shared/ui/Card';
import { DateTime } from '@/shared/ui/DateTime';

import { botKeys, botUnresolvedOrdersQuery, confirmOrderAbsent } from '../api/botsApi';

/**
 * Submits whose delivery to the venue is unknown, and orders the venue accepted but no longer knows (an expired day
 * order). For an accepted order the bot checks the venue's own records itself and closes the order when every check holds
 * (ADR 0030); the owner's confirmation is the fallback. The owner may confirm one absent after checking the account on the
 * venue; restarting the bot then looks it up again and closes it only when the venue shows nothing. Shown only while there
 * are any.
 */
export function BotUnresolvedOrders({ botId }: { botId: string }) {
  const queryClient = useQueryClient();
  const unresolved = useQuery(botUnresolvedOrdersQuery(botId));
  const [confirming, setConfirming] = useState<string | null>(null);
  const confirm = useMutation({
    mutationFn: (intentId: string) => confirmOrderAbsent(botId, intentId),
    onSuccess: () => {
      setConfirming(null);

      return Promise.all([
        queryClient.invalidateQueries({ queryKey: botKeys.unresolved(botId) }),
        queryClient.invalidateQueries({ queryKey: botKeys.feed(botId) }),
      ]);
    },
  });

  if (!unresolved.data || unresolved.data.length === 0) {
    return null;
  }

  return (
    <Card title="Неясные заявки">
      <ul className="space-y-3 text-sm" aria-label="Неясные заявки">
        {unresolved.data.map((order) => (
          <li key={order.intentId} className="space-y-2">
            <p className="flex flex-wrap gap-x-3">
              <span className="font-mono">заявка {order.engineOrderId}</span>
              <span>
                {order.side === 'Buy' ? 'покупка' : 'продажа'}
                {order.quoteBudget && ` на ${order.quoteBudget}`}
                {order.quantity && `, ${order.quantity} шт.`}
              </span>
              {order.placeStartedAt && (
                <span className="text-muted">
                  отправлена <DateTime value={order.placeStartedAt} />
                </span>
              )}
            </p>
            {order.acceptedByVenue && !order.confirmedAbsentAt && order.canConfirmAbsent && (
              <p className="text-muted">
                Бот проверяет такие заявки сам, каждые 5 минут, и закроет эту без вашего
                подтверждения, когда выполнится всё: биржа её не знает, торговый день закончился, в
                операциях по счёту нет исполнений, а позиция по бумаге равна ожидаемой. Если он её
                не закрыл, причина записана в ленте. Подтверждение ниже — запасной путь.
              </p>
            )}
            {order.confirmedAbsentAt ? (
              <p className="text-muted">
                {order.acceptedByVenue
                  ? 'Снятие заявки биржей подтверждено'
                  : 'Отсутствие подтверждено'}{' '}
                <DateTime value={order.confirmedAbsentAt} />. Нажмите «Перезапустить»: бот ещё раз
                проверит биржу и закроет заявку, только если её там нет.
              </p>
            ) : order.canConfirmAbsent ? (
              confirming === order.intentId ? (
                <div className="space-y-2">
                  <p className="text-muted">
                    {order.acceptedByVenue
                      ? 'Биржа приняла эту заявку, но теперь её не знает: так бывает, когда дневную заявку снимают в конце торгового дня, пока бот не работает. Подтверждайте, только если в приложении брокера заявки нет среди активных, а в операциях и в позиции нет её исполнения, даже частичного.'
                      : 'Подтверждайте, только если на бирже нет ни этой заявки, ни её исполнения: ни запроса на покупку, ни покупки в истории, ни ожидающего подтверждения.'}
                  </p>
                  <div className="flex flex-wrap gap-2">
                    <Button
                      disabled={confirm.isPending}
                      onClick={() => confirm.mutate(order.intentId)}
                    >
                      {order.acceptedByVenue
                        ? 'Подтвердить: заявка снята, исполнения не было'
                        : 'Подтвердить: заявки на бирже нет'}
                    </Button>
                    <Button variant="ghost" onClick={() => setConfirming(null)}>
                      Отмена
                    </Button>
                  </div>
                </div>
              ) : (
                <Button variant="ghost" onClick={() => setConfirming(order.intentId)}>
                  {order.acceptedByVenue ? 'Заявка снята биржей' : 'Заявки на бирже нет'}
                </Button>
              )
            ) : order.acceptedByVenue ? (
              <p className="text-muted">
                Подтвердить нельзя: по заявке уже есть исполнение. Сверьте её в приложении брокера.
              </p>
            ) : (
              <p className="text-muted">Бот ждёт, пока биржа покажет, что стало с заявкой.</p>
            )}
          </li>
        ))}
      </ul>
      {confirm.isError && (
        <ApiErrorMessage
          error={confirm.error}
          fallback="Не удалось подтвердить отсутствие заявки."
        />
      )}
    </Card>
  );
}
