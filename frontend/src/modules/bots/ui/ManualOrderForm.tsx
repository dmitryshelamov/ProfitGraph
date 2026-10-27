import { useMutation, useQuery } from '@tanstack/react-query';
import { type FormEvent, useState } from 'react';

import { instrumentRulesQuery } from '@/modules/market-data';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { Field } from '@/shared/ui/Field';
import { Input } from '@/shared/ui/Input';

import { manualOrderQuery, submitManualOrder } from '../api/botsApi';
import { checkManualOrder, type ManualOrderType, rejectionLabel } from '../model/manualOrder';
import type { Bot, OrderSide } from '../model/types';
import { useBotDisplay } from '../model/useBotDisplay';

const statusLabels = {
  Pending: 'ждёт бота',
  Submitted: 'передан в работу',
  Rejected: 'отклонён',
} as const;

const sideLabels: Record<OrderSide, string> = { Buy: 'купить', Sell: 'продать' };

/**
 * An owner order in a running bot, market or limit (ADR 0027): a buy is sized in quote, a sell in base. The bot takes it
 * within a minute; the strategy sees it only through its global order listeners. A limit order is confirmed before it is
 * sent, because it rests on the venue until it fills and the system cannot cancel it yet.
 */
export function ManualOrderForm({ bot }: { bot: Bot }) {
  const [type, setType] = useState<ManualOrderType>('Market');
  const [side, setSide] = useState<OrderSide>('Buy');
  const [amount, setAmount] = useState('');
  const [price, setPrice] = useState('');
  const [confirming, setConfirming] = useState(false);
  const limit = type === 'Limit';
  const submit = useMutation({
    mutationFn: () =>
      submitManualOrder(bot.id, {
        side,
        amount: amount.trim(),
        ...(limit ? { limitPrice: price.trim() } : {}),
      }),
    onSettled: () => setConfirming(false),
  });
  const order = useQuery({
    ...manualOrderQuery(bot.id, submit.data?.id ?? ''),
    enabled: submit.data !== undefined,
  });
  const rules = useQuery(instrumentRulesQuery(bot.venue, bot.symbol, bot.credentialId));
  const display = useBotDisplay(bot);
  const running = bot.desiredState === 'Running' && bot.runtimeStatus === 'Running';
  const check = checkManualOrder({ type, side, amount, price }, rules.data);
  const waiting = submit.isPending || order.data?.status === 'Pending';
  const complete = amount.trim() !== '' && (!limit || price.trim() !== '');
  const canSend = running && complete && check.error === null && !waiting;
  const amountAsset = side === 'Buy' ? display.quoteAsset : display.baseAsset;
  const amountLabel = withAsset(side === 'Buy' ? 'Сумма' : 'Количество', amountAsset);
  const priceLabel = withAsset('Цена за 1 шт.', display.quoteAsset);

  function send(event: FormEvent) {
    event.preventDefault();

    if (!canSend) {
      return;
    }

    if (limit && !confirming) {
      setConfirming(true);

      return;
    }

    submit.mutate();
  }

  function change(update: () => void) {
    update();
    setConfirming(false);
  }

  return (
    <form onSubmit={send} className="space-y-3" aria-label="Ручная заявка">
      <div className="grid gap-3 md:grid-cols-2">
        <Field label="Тип заявки">
          <select
            className="rounded-md border border-line bg-panel px-3 py-2 text-sm"
            value={type}
            onChange={(event) => change(() => setType(event.target.value as ManualOrderType))}
          >
            <option value="Market">Рыночная</option>
            <option value="Limit">Лимитная</option>
          </select>
        </Field>
        <Field label="Сторона">
          <select
            className="rounded-md border border-line bg-panel px-3 py-2 text-sm"
            value={side}
            onChange={(event) => change(() => setSide(event.target.value as OrderSide))}
          >
            <option value="Buy">Купить</option>
            <option value="Sell">Продать</option>
          </select>
        </Field>
        <Field label={amountLabel}>
          <Input
            value={amount}
            onChange={(event) => change(() => setAmount(event.target.value))}
            inputMode="decimal"
            required
          />
        </Field>
        {limit && (
          <Field label={priceLabel}>
            <Input
              value={price}
              onChange={(event) => change(() => setPrice(event.target.value))}
              inputMode="decimal"
              required
            />
          </Field>
        )}
      </div>
      {limit && rules.data && (
        <p className="text-sm text-muted">
          Шаг цены {rules.data.priceTick}, шаг количества (лот) {rules.data.quantityStep}, минимум{' '}
          {rules.data.minQuantity}. Количество округляется вниз до шага.
        </p>
      )}
      {limit && check.quantity !== null && check.error === null && (
        <p className="text-sm">Будет выставлено количество: {check.quantity}</p>
      )}
      {check.error && (
        <p className="text-sm text-red-600" role="alert">
          {check.error}
        </p>
      )}
      {confirming && (
        <p className="text-sm" role="alert">
          Подтвердите лимитную заявку: {sideLabels[side]}
          {check.quantity !== null && ` ${check.quantity}`} {display.baseAsset ?? ''} по цене{' '}
          {price.trim()} {display.quoteAsset ?? ''}
          {side === 'Buy' && ` на сумму не более ${amount.trim()} ${amountAsset ?? ''}`}. Заявка
          будет стоять на бирже до исполнения; отменить её из ProfitGraph пока нельзя.
        </p>
      )}
      <div className="flex gap-2">
        <Button type="submit" size="sm" disabled={!canSend}>
          {confirming ? 'Подтвердить и отправить' : 'Отправить заявку'}
        </Button>
        {confirming && (
          <Button type="button" size="sm" onClick={() => setConfirming(false)}>
            Назад
          </Button>
        )}
      </div>
      {!running && <p className="text-sm text-muted">Ручная заявка доступна, пока бот работает.</p>}
      {order.data && (
        <p className="text-sm" role="status">
          Заявка {statusLabels[order.data.status]}
          {order.data.rejectionReason && `: ${rejectionLabel(order.data.rejectionReason)}`}
          {order.data.type === 'Limit' &&
            order.data.limitPrice &&
            ` (лимитная, цена ${order.data.limitPrice})`}
        </p>
      )}
      {submit.isError && (
        <ApiErrorMessage error={submit.error} fallback="Не удалось отправить заявку." />
      )}
    </form>
  );
}

/** A field label with the asset its value is in, when the asset is known: «Сумма, RUB». */
function withAsset(label: string, asset: string | null): string {
  return asset ? `${label}, ${asset}` : label;
}
