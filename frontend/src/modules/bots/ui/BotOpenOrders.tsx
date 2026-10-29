import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';

import { ApiError } from '@/shared/api/client';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';

import {
  botKeys,
  botOpenOrdersQuery,
  botWaitingOrdersQuery,
  cancelBotOrder,
  orderCancelQuery,
} from '../api/botsApi';
import { atPlaces, type BotDisplay } from '../model/botDisplay';
import {
  canCancelOpenOrder,
  cancelProblemLabel,
  openOrderPrice,
  openOrderQuantity,
  openOrderState,
  orderCancelMode,
  orderCancelState,
} from '../model/openOrders';
import type { Bot, BotOpenOrder } from '../model/types';

const cancelErrorCodes = new Set([
  'order_not_open',
  'order_cancel_pending',
  'order_cancel_request_pending',
  'order_not_sent',
  'bot_not_running',
]);

/**
 * The bot's open orders. While the bot runs, the owner may cancel one of them (ADR 0048): after a confirmation the request
 * waits for the bot, which cancels the order as it cancels its own, and the block follows it until the order is gone or
 * the request is rejected; the strategy then places the missing orders again (ADR 0051). Once the bot is stopped, the cancel
 * goes to the exchange at once and the strategy restores its orders at the next start (ADR 0055).
 */
export function BotOpenOrders({ bot, display }: { bot: Bot; display: BotDisplay }) {
  const botId = bot.id;
  const queryClient = useQueryClient();
  const [confirming, setConfirming] = useState<number | null>(null);
  const cancel = useMutation({
    mutationFn: (engineOrderId: number) => cancelBotOrder(botId, engineOrderId),
    onSettled: () => setConfirming(null),
  });
  const followed = useQuery({
    ...orderCancelQuery(botId, cancel.data?.id ?? ''),
    enabled: cancel.data !== undefined,
  });
  const request = followed.data ?? cancel.data;
  const orders = useQuery({
    ...botOpenOrdersQuery(botId),
    refetchInterval: (query) =>
      request?.status === 'Requested' &&
      (query.state.data ?? []).some((order) => order.engineOrderId === request.engineOrderId)
        ? 1_000
        : 15_000,
  });
  const waiting = useQuery(botWaitingOrdersQuery(botId)).data ?? [];
  const state = request ? orderCancelState(request, orders.data ?? []) : null;
  const mode = orderCancelMode(bot);
  const cancellable = mode !== null;
  const busy = cancel.isPending || (state !== null && !state.settled);
  const answered =
    request && request.status !== 'Pending'
      ? `${request.id}:${request.status}:${state?.settled ? 'settled' : 'open'}`
      : null;

  // The bot's answer changes the open orders (and the chart's levels, which read the same query), the waits and the feed.
  useEffect(() => {
    if (answered === null) {
      return;
    }

    void Promise.all([
      queryClient.invalidateQueries({ queryKey: botKeys.openOrders(botId) }),
      queryClient.invalidateQueries({ queryKey: botKeys.waiting(botId) }),
      queryClient.invalidateQueries({ queryKey: botKeys.feed(botId) }),
    ]);
  }, [answered, botId, queryClient]);

  if (orders.isError) {
    return (
      <p role="alert" className="text-danger-text">
        Не удалось загрузить активные ордера.
      </p>
    );
  }

  const target = orders.data?.find((order) => order.engineOrderId === confirming);
  const status = (
    <>
      {state && (
        <p className="text-sm" role="status">
          {state.text}
        </p>
      )}
      {cancel.isError && <CancelError error={cancel.error} />}
    </>
  );

  if (!orders.data || orders.data.length === 0) {
    return (
      <div className="space-y-3">
        <p className="text-muted">{orders.isPending ? 'Загрузка…' : 'Активных ордеров нет.'}</p>
        {status}
      </div>
    );
  }

  return (
    <div className="space-y-3">
      <table className="w-full text-left text-sm" aria-label="Активные ордера бота">
        <thead className="text-text-secondary">
          <tr>
            <th className="py-2">Ордер</th>
            <th className="py-2">Сторона</th>
            <th className="py-2">Тип</th>
            <th className="py-2">Количество{display.baseAsset ? `, ${display.baseAsset}` : ''}</th>
            <th className="py-2">Цена{display.quoteAsset ? `, ${display.quoteAsset}` : ''}</th>
            <th className="py-2">Исполнено{display.baseAsset ? `, ${display.baseAsset}` : ''}</th>
            <th className="py-2">Состояние</th>
            {cancellable && (
              <th className="py-2">
                <span className="sr-only">Действие</span>
              </th>
            )}
          </tr>
        </thead>
        <tbody>
          {orders.data.map((order) => {
            const orderState = openOrderState(order, waiting);
            const cancelling = busy && request?.engineOrderId === order.engineOrderId;

            return (
              <tr key={order.engineOrderId} className="border-t border-line">
                <td className="py-2">
                  <span className="font-mono">{order.engineOrderId}</span>
                  {order.tag ? <span className="ml-2 text-muted">{order.tag}</span> : null}
                </td>
                <td className="py-2">{order.side === 'Buy' ? 'Покупка' : 'Продажа'}</td>
                <td className="py-2">{order.type === 'Limit' ? 'Лимитный' : 'Рыночный'}</td>
                <td className="py-2 font-mono">{quantity(order, display)}</td>
                <td className="py-2 font-mono">{price(order, display)}</td>
                <td className="py-2 font-mono">{atPlaces(order.filledBase, display.basePlaces)}</td>
                <td className="py-2">
                  {orderState.label}
                  {orderState.reason ? (
                    <span className="block text-muted">{orderState.reason}</span>
                  ) : null}
                </td>
                {cancellable && (
                  <td className="py-2 text-right">
                    {cancelling ? (
                      <span className="text-muted">Отмена…</span>
                    ) : canCancelOpenOrder(order) ? (
                      <Button
                        size="sm"
                        variant="ghost"
                        disabled={busy || confirming === order.engineOrderId}
                        aria-label={`Отменить ордер ${order.engineOrderId}`}
                        onClick={() => setConfirming(order.engineOrderId)}
                      >
                        Отменить
                      </Button>
                    ) : null}
                  </td>
                )}
              </tr>
            );
          })}
        </tbody>
      </table>
      {cancellable && target && (
        <div className="space-y-2 text-sm" role="alert">
          <p>
            Отменить ордер {target.engineOrderId}
            {target.tag ? ` (${target.tag})` : ''}: {target.side === 'Buy' ? 'покупка' : 'продажа'}{' '}
            {quantity(target, display)} {display.baseAsset ?? ''}, цена {price(target, display)}?
            {mode === 'stopped'
              ? ' Бот остановлен: биржу попросят снять ордер сразу; уже исполненная часть останется в позиции.'
              : ' Бот попросит биржу снять ордер; уже исполненная часть останется в позиции.'}
          </p>
          <p className="text-muted">
            {mode === 'stopped'
              ? 'При следующем запуске стратегия выставит недостающие ордера заново по своим правилам, по текущей позиции. Ручная заявка заново не выставляется.'
              : 'Когда биржа подтвердит отмену, стратегия выставит недостающие ордера заново по своим правилам, по текущей позиции. Ручная заявка заново не выставляется.'}
          </p>
          <div className="flex gap-2">
            <Button size="sm" disabled={busy} onClick={() => cancel.mutate(target.engineOrderId)}>
              Подтвердить отмену
            </Button>
            <Button size="sm" variant="ghost" onClick={() => setConfirming(null)}>
              Назад
            </Button>
          </div>
        </div>
      )}
      {!cancellable && orders.data.some(canCancelOpenOrder) && (
        <p className="text-sm text-muted">
          Отменить ордер можно, пока бот работает или после его остановки.
        </p>
      )}
      {status}
    </div>
  );
}

function quantity(order: BotOpenOrder, display: BotDisplay): string {
  return openOrderQuantity({
    quantity: order.quantity ? atPlaces(order.quantity, display.basePlaces) : order.quantity,
    quoteBudget: order.quoteBudget
      ? atPlaces(order.quoteBudget, display.quotePlaces)
      : order.quoteBudget,
  });
}

function price(order: BotOpenOrder, display: BotDisplay): string {
  return openOrderPrice({
    type: order.type,
    limitPrice: order.limitPrice
      ? atPlaces(order.limitPrice, display.pricePlaces)
      : order.limitPrice,
  });
}

function CancelError({ error }: { error: Error }) {
  if (error instanceof ApiError && cancelErrorCodes.has(error.code)) {
    return (
      <p role="alert" className="text-sm text-danger-text">
        Не удалось отменить ордер: {cancelProblemLabel(error.code)}.
      </p>
    );
  }

  return <ApiErrorMessage error={error} fallback="Не удалось отменить ордер." />;
}
