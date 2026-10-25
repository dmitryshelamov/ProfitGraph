import { useMutation, useQuery } from '@tanstack/react-query';
import { type FormEvent, useState } from 'react';

import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { Field } from '@/shared/ui/Field';
import { Input } from '@/shared/ui/Input';

import { manualOrderQuery, submitManualOrder } from '../api/botsApi';
import type { Bot, OrderSide } from '../model/types';

const statusLabels = {
  Pending: 'ждёт бота',
  Submitted: 'передан в работу',
  Rejected: 'отклонён',
} as const;

/**
 * An owner market order in a running bot: a buy is sized in quote, a sell in base. The bot takes it within a minute;
 * the strategy sees it only through its global order listeners.
 */
export function ManualOrderForm({ bot }: { bot: Bot }) {
  const [side, setSide] = useState<OrderSide>('Buy');
  const [amount, setAmount] = useState('');
  const submit = useMutation({ mutationFn: () => submitManualOrder(bot.id, side, amount.trim()) });
  const order = useQuery({
    ...manualOrderQuery(bot.id, submit.data?.id ?? ''),
    enabled: submit.data !== undefined,
  });
  const running = bot.desiredState === 'Running' && bot.runtimeStatus === 'Running';

  function send(event: FormEvent) {
    event.preventDefault();
    submit.mutate();
  }

  return (
    <form onSubmit={send} className="space-y-3" aria-label="Ручная заявка">
      <div className="grid gap-3 md:grid-cols-2">
        <Field label="Сторона">
          <select
            className="rounded-md border border-line bg-panel px-3 py-2 text-sm"
            value={side}
            onChange={(event) => setSide(event.target.value as OrderSide)}
          >
            <option value="Buy">Купить (сумма в котируемой валюте)</option>
            <option value="Sell">Продать (количество в базовой валюте)</option>
          </select>
        </Field>
        <Field label="Сумма">
          <Input
            value={amount}
            onChange={(event) => setAmount(event.target.value)}
            inputMode="decimal"
            required
          />
        </Field>
      </div>
      <Button type="submit" size="sm" disabled={!running || submit.isPending}>
        Отправить заявку
      </Button>
      {!running && <p className="text-sm text-muted">Ручная заявка доступна, пока бот работает.</p>}
      {order.data && (
        <p className="text-sm" role="status">
          Заявка {statusLabels[order.data.status]}
          {order.data.rejectionReason && `: ${order.data.rejectionReason}`}
        </p>
      )}
      {submit.isError && (
        <ApiErrorMessage error={submit.error} fallback="Не удалось отправить заявку." />
      )}
    </form>
  );
}
