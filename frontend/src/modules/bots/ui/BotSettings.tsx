import { useMutation, useQueryClient } from '@tanstack/react-query';
import { type FormEvent, useState } from 'react';

import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { Field } from '@/shared/ui/Field';
import { Input } from '@/shared/ui/Input';

import { botKeys, changeForeignOrders, changeParameters, changeRiskLimits } from '../api/botsApi';
import { canEditParameters } from '../model/botStatus';
import type { Bot } from '../model/types';

function useBotChange(bot: Bot) {
  const queryClient = useQueryClient();

  return (action: () => Promise<Bot>) => ({
    mutationFn: action,
    onSuccess: (updated: Bot) => {
      queryClient.setQueryData(botKeys.detail(bot.id), updated);

      return queryClient.invalidateQueries({ queryKey: botKeys.all });
    },
  });
}

/** The per-order and daily loss limits change at any time. */
export function RiskLimitsForm({ bot }: { bot: Bot }) {
  const [maxOrderQuote, setMaxOrderQuote] = useState(bot.maxOrderQuote);
  const [dailyLossLimitQuote, setDailyLossLimitQuote] = useState(bot.dailyLossLimitQuote);
  const save = useMutation(
    useBotChange(bot)(() =>
      changeRiskLimits(bot.id, maxOrderQuote.trim(), dailyLossLimitQuote.trim()),
    ),
  );

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate();
  }

  return (
    <form onSubmit={submit} className="space-y-3" aria-label="Лимиты риска">
      <div className="grid gap-3 md:grid-cols-2">
        <Field label="Лимит на заявку">
          <Input
            value={maxOrderQuote}
            onChange={(event) => setMaxOrderQuote(event.target.value)}
            inputMode="decimal"
          />
        </Field>
        <Field label="Дневной лимит убытка">
          <Input
            value={dailyLossLimitQuote}
            onChange={(event) => setDailyLossLimitQuote(event.target.value)}
            inputMode="decimal"
          />
        </Field>
      </div>
      <Button type="submit" size="sm" disabled={save.isPending}>
        Сохранить лимиты
      </Button>
      {save.isError && (
        <ApiErrorMessage error={save.error} fallback="Не удалось сохранить лимиты." />
      )}
    </form>
  );
}

/** Parameters and the bot's capital change only while it is stopped; they apply from its next start. */
export function ParametersForm({ bot }: { bot: Bot }) {
  const [values, setValues] = useState<Record<string, string>>({ ...bot.parameters });
  const [allocationCap, setAllocationCap] = useState(bot.allocationCap);
  const save = useMutation(
    useBotChange(bot)(() => changeParameters(bot.id, values, allocationCap.trim())),
  );
  const editable = canEditParameters(bot);

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate();
  }

  return (
    <form onSubmit={submit} className="space-y-3" aria-label="Параметры бота">
      <div className="grid gap-3 md:grid-cols-2">
        {Object.keys(bot.parameters).map((name) => (
          <Field key={name} label={name}>
            <Input
              value={values[name] ?? ''}
              onChange={(event) =>
                setValues((current) => ({ ...current, [name]: event.target.value }))
              }
              disabled={!editable}
              inputMode="decimal"
            />
          </Field>
        ))}
        <Field label="Капитал бота">
          <Input
            value={allocationCap}
            onChange={(event) => setAllocationCap(event.target.value)}
            disabled={!editable}
            inputMode="decimal"
          />
        </Field>
      </div>
      {editable ? (
        <Button type="submit" size="sm" disabled={save.isPending}>
          Сохранить параметры
        </Button>
      ) : (
        <p className="text-sm text-muted">Параметры меняются, когда бот остановлен.</p>
      )}
      {save.isError && (
        <ApiErrorMessage error={save.error} fallback="Не удалось сохранить параметры." />
      )}
    </form>
  );
}

/** Whether the bot tolerates orders of other sources on its symbol, such as manual trading; changes while stopped. */
export function ForeignOrdersToggle({ bot }: { bot: Bot }) {
  const save = useMutation(
    useBotChange(bot)(() => changeForeignOrders(bot.id, !bot.allowForeignOrders)),
  );

  return (
    <div className="space-y-2">
      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          checked={bot.allowForeignOrders}
          disabled={!canEditParameters(bot) || save.isPending}
          onChange={() => save.mutate()}
        />
        Разрешить чужие заявки на символе
      </label>
      {save.isError && (
        <ApiErrorMessage error={save.error} fallback="Не удалось изменить настройку." />
      )}
    </div>
  );
}
