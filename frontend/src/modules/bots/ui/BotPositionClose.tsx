import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';

import { compareDecimals, multiplyDecimals } from '@/shared/lib/decimal';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';

import {
  botKeys,
  botOpenOrdersQuery,
  botPositionCloseQuery,
  botPositionQuery,
  requestPositionClose,
} from '../api/botsApi';
import { atPlaces, type BotDisplay, exactAmount, withAsset } from '../model/botDisplay';
import { openOrderQuantity } from '../model/openOrders';
import { closeOutcome, closeSteps, isActiveClose } from '../model/positionClose';
import type { Bot } from '../model/types';

/** How long an ended close stays on the card. */
const shownAfterEnd = 30 * 60_000;

const stepMarks = { done: '✓', active: '…', waiting: '·' } as const;

/**
 * The owner closes the bot's long position (ADR 0064): after a confirmation that names what is sold, roughly at what price and
 * which of the bot's orders are cancelled first, the bot's runner cancels them, sells at the market and stops the bot unless
 * the owner keeps it running. The card follows the close's steps and keeps its outcome for a while.
 */
export function BotPositionClose({ bot, display }: { bot: Bot; display: BotDisplay }) {
  const queryClient = useQueryClient();
  const position = useQuery(botPositionQuery(bot.id));
  const orders = useQuery(botOpenOrdersQuery(bot.id));
  const close = useQuery(botPositionCloseQuery(bot.id));
  const [confirming, setConfirming] = useState(false);
  const [stopAfter, setStopAfter] = useState(true);
  const request = useMutation({
    mutationFn: () => requestPositionClose(bot.id, stopAfter),
    onSuccess: (started) => {
      queryClient.setQueryData(botKeys.positionClose(bot.id), started);
      setConfirming(false);
    },
  });
  const current = close.data ?? null;
  const active = current !== null && isActiveClose(current);
  const wasActive = useRef(active);

  // A close that just ended changed the bot, its orders, position, fills and feed.
  useEffect(() => {
    if (wasActive.current && !active) {
      void queryClient.invalidateQueries({ queryKey: botKeys.detail(bot.id) });
    }

    wasActive.current = active;
  }, [active, bot.id, queryClient]);

  const data = position.data;
  const running = bot.desiredState === 'Running' && bot.runtimeStatus === 'Running';
  const open =
    data !== undefined && data.openedAt !== null && compareDecimals(data.baseTotal, '0') === 1;
  const recentlyEnded =
    current?.resolvedAt != null && Date.now() - Date.parse(current.resolvedAt) < shownAfterEnd;

  if (current && (active || recentlyEnded)) {
    return (
      <section
        aria-label="Закрытие позиции"
        className="space-y-2 rounded-md border border-line p-3"
      >
        <h3 className="text-sm font-medium">
          {active
            ? 'Закрытие позиции'
            : current.status === 'Closed'
              ? 'Позиция закрыта'
              : 'Закрытие не удалось'}
        </h3>
        {active ? (
          <ol className="space-y-1 text-sm">
            {closeSteps(current).map((item) => (
              <li
                key={item.label}
                data-state={item.state}
                className={item.state === 'waiting' ? 'text-muted' : ''}
              >
                <span aria-hidden="true" className="mr-2 inline-block w-3">
                  {stepMarks[item.state]}
                </span>
                {item.label}
              </li>
            ))}
          </ol>
        ) : (
          <p
            role="status"
            className={`text-sm ${current.status === 'Failed' ? 'text-danger-text' : 'text-text-secondary'}`}
          >
            {closeOutcome(current)}
          </p>
        )}
        <p className="text-xs text-muted">Ход закрытия виден и в ленте.</p>
      </section>
    );
  }

  if (!running || !open || data === undefined) {
    return null;
  }

  if (!confirming) {
    return (
      <button
        type="button"
        onClick={() => setConfirming(true)}
        className="w-full rounded-md border border-danger-border px-3 py-2 text-sm text-danger-text hover:bg-danger-bg"
      >
        Закрыть позицию
      </button>
    );
  }

  const price = data.markPrice;
  const value = price === null ? null : multiplyDecimals(data.baseTotal, price);
  const fee = value === null ? null : multiplyDecimals(value, bot.marketFeeRate);
  const quote = (amount: string) =>
    withAsset(atPlaces(amount, display.quotePlaces), display.quoteAsset);
  const working = orders.data ?? [];

  return (
    <section
      aria-label="Подтверждение закрытия позиции"
      className="space-y-3 rounded-md border border-danger-border p-3 text-sm"
    >
      <h3 className="font-medium">Закрыть позицию по рынку?</h3>
      <dl className="space-y-1">
        <div className="flex justify-between gap-3">
          <dt className="text-text-secondary">Продать</dt>
          <dd className="font-mono">{withAsset(exactAmount(data.baseTotal), display.baseAsset)}</dd>
        </div>
        {price !== null && (
          <div className="flex justify-between gap-3">
            <dt className="text-text-secondary">≈ по цене</dt>
            <dd className="font-mono">
              {withAsset(atPlaces(price, display.pricePlaces), display.quoteAsset)}
            </dd>
          </div>
        )}
        {value !== null && (
          <div className="flex justify-between gap-3">
            <dt className="text-text-secondary">≈ выручка</dt>
            <dd className="font-mono">
              {quote(value)}
              {fee !== null && ` − комиссия ${quote(fee)}`}
            </dd>
          </div>
        )}
      </dl>
      {working.length > 0 ? (
        <div>
          <p>Сначала будут отменены ордера бота: {working.length}</p>
          <ul className="text-text-secondary" aria-label="Ордера к отмене">
            {working.map((order) => (
              <li key={order.engineOrderId}>
                {order.side === 'Buy' ? 'Покупка' : 'Продажа'}
                {order.limitPrice !== null &&
                  ` ${atPlaces(order.limitPrice, display.pricePlaces)}`}{' '}
                × {openOrderQuantity(order)}
              </li>
            ))}
          </ul>
        </div>
      ) : (
        <p className="text-text-secondary">Ордеров для отмены нет.</p>
      )}
      <fieldset className="space-y-1">
        <legend className="mb-1">После закрытия</legend>
        <label className="flex items-start gap-2">
          <input
            type="radio"
            name="after-close"
            checked={stopAfter}
            onChange={() => setStopAfter(true)}
          />
          <span>
            Остановить бота
            <span className="block text-xs text-muted">стратегия не войдёт снова</span>
          </span>
        </label>
        <label className="flex items-start gap-2">
          <input
            type="radio"
            name="after-close"
            checked={!stopAfter}
            onChange={() => setStopAfter(false)}
          />
          <span>
            Бот работает дальше
            <span className="block text-xs text-muted">
              стратегия может войти по своему сигналу
            </span>
          </span>
        </label>
      </fieldset>
      <p className="text-xs text-muted">
        Пока позиция закрывается, стратегия не выставляет заявок. Если продажа не удастся, бот
        остановится.
      </p>
      {request.isError && (
        <ApiErrorMessage error={request.error} fallback="Не удалось начать закрытие позиции." />
      )}
      <div className="flex gap-2">
        <button
          type="button"
          disabled={request.isPending}
          onClick={() => request.mutate()}
          className="flex-1 rounded-md border border-danger-border bg-danger-bg px-3 py-1.5 font-medium text-danger-text disabled:opacity-50"
        >
          Закрыть по рынку
        </button>
        <Button size="sm" variant="ghost" onClick={() => setConfirming(false)}>
          Отмена
        </Button>
      </div>
    </section>
  );
}
