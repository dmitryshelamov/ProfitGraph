import { useQueryClient } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';

import { ApiError } from '@/shared/api/client';
import { addDecimals, compareDecimals } from '@/shared/lib/decimal';
import { Button } from '@/shared/ui/Button';

import { createScanTrade, scannerKeys } from '../api/scannerApi';
import { formatMetric } from '../model/format';
import { tradeScenarioHints, tradeScenarioLabels } from '../model/trades';
import type { ScanTradeIdea, ScanTradeIdeas } from '../model/types';

const createErrors: Record<string, string> = {
  scan_trade_live: 'Такая сделка уже живёт.',
  scan_trade_entry_moved: 'Цена ушла от входа — карточки обновятся.',
  scan_trade_pair_not_scanned: 'Пары нет в последнем снимке сканера.',
  scan_trade_settings_missing: 'Сначала задайте риск и ключ.',
};

type Outcome = { readonly botId: string } | { readonly error: string };

/**
 * The trade cards of a pair on every timeframe (ADR 0060), grouped by the timeframe whose levels gave them. The owner ticks the
 * wanted ones and «Создать выбранные» creates a bot with real orders for each: it buys at the entry, sells at the target and sells by
 * market when a candle closes at the stop. «Скрыть» hides a card until the cards change; a card a live trade already took cannot be
 * ticked.
 */
export function ScanTradeCards({ venue, ideas }: { venue: string; ideas: ScanTradeIdeas }) {
  const queryClient = useQueryClient();
  const [hidden, setHidden] = useState<readonly string[]>([]);
  const [selected, setSelected] = useState<readonly string[]>([]);
  const [outcomes, setOutcomes] = useState<Readonly<Record<string, Outcome>>>({});
  const [creating, setCreating] = useState(false);
  const visible = ideas.ideas.filter((idea) => !hidden.includes(key(idea)));
  const chosen = visible.filter(
    (idea) => selected.includes(key(idea)) && !idea.taken && !outcomes[key(idea)],
  );
  const timeframes = [...new Set(visible.map((idea) => idea.timeframe))];

  if (ideas.ideas.length === 0) {
    return (
      <p className="text-sm text-muted">
        Идей нет: ни на одном таймфрейме нет поддержки ниже цены, а последний час не рос.
      </p>
    );
  }

  async function createChosen() {
    setCreating(true);

    for (const idea of chosen) {
      try {
        const trade = await createScanTrade(venue, ideas.symbol, idea);
        setOutcomes((current) => ({ ...current, [key(idea)]: { botId: trade.botId } }));
      } catch (error) {
        const message =
          error instanceof ApiError
            ? (createErrors[error.code] ?? error.message)
            : 'Не удалось создать сделку.';
        setOutcomes((current) => ({ ...current, [key(idea)]: { error: message } }));
      }
    }

    setSelected([]);
    setCreating(false);
    await queryClient.invalidateQueries({ queryKey: scannerKeys.tradeIdeas(venue, ideas.symbol) });
    await queryClient.invalidateQueries({ queryKey: scannerKeys.trades(venue) });
  }

  function toggle(idea: ScanTradeIdea, on: boolean) {
    setSelected((current) =>
      on ? [...current, key(idea)] : current.filter((item) => item !== key(idea)),
    );
  }

  return (
    <div className="space-y-4">
      {timeframes.map((timeframe) => (
        <section key={timeframe} aria-label={`Идеи по ${timeframe}`} className="space-y-2">
          <h3 className="text-sm font-semibold text-text-secondary">Уровни {timeframe}</h3>
          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
            {visible
              .filter((idea) => idea.timeframe === timeframe)
              .map((idea) => (
                <TradeCard
                  key={key(idea)}
                  idea={idea}
                  checked={selected.includes(key(idea))}
                  outcome={outcomes[key(idea)]}
                  disabled={creating || !ideas.hasSettings}
                  onToggle={(on) => toggle(idea, on)}
                  onHide={() => setHidden((current) => [...current, key(idea)])}
                />
              ))}
          </div>
        </section>
      ))}
      {visible.length === 0 && (
        <p className="text-sm text-muted">Все карточки скрыты; они вернутся с новыми ценами.</p>
      )}
      <div className="flex flex-wrap items-center gap-3 border-t border-line pt-3">
        <Button
          variant="primary"
          size="sm"
          disabled={chosen.length === 0 || creating || !ideas.hasSettings}
          onClick={() => void createChosen()}
        >
          {creating ? 'Создаю…' : `Создать выбранные (${chosen.length})`}
        </Button>
        {chosen.length > 0 && (
          <span className="text-sm text-text-secondary">
            Сумма{' '}
            {formatMetric(
              total(chosen, (idea) => idea.amount),
              2,
            )}{' '}
            USDT, потеря на всех стопах{' '}
            {formatMetric(
              total(chosen, (idea) => idea.risk),
              2,
            )}{' '}
            USDT
          </span>
        )}
        {selected.length > 0 && !creating && (
          <Button size="sm" variant="ghost" onClick={() => setSelected([])}>
            Сбросить
          </Button>
        )}
      </div>
    </div>
  );
}

function TradeCard({
  idea,
  checked,
  outcome,
  disabled,
  onToggle,
  onHide,
}: {
  idea: ScanTradeIdea;
  checked: boolean;
  outcome: Outcome | undefined;
  disabled: boolean;
  onToggle: (on: boolean) => void;
  onHide: () => void;
}) {
  const label = tradeScenarioLabels[idea.scenario] ?? idea.scenario;
  const goodRewardRisk = (compareDecimals(idea.rewardRisk, '2') ?? -1) >= 0;
  const hours = idea.entryValidityMinutes / 60;
  const locked = idea.taken || outcome !== undefined;

  return (
    <article
      aria-label={`Идея: ${label} ${idea.timeframe}`}
      className={`flex flex-col gap-3 rounded-lg border bg-panel-2 p-4 ${checked && !locked ? 'border-link' : 'border-line-strong'}`}
    >
      <header className="flex items-start justify-between gap-2">
        <label className="flex items-start gap-2">
          <input
            type="checkbox"
            className="mt-1"
            checked={checked && !locked}
            disabled={disabled || locked || !idea.amount}
            onChange={(event) => onToggle(event.target.checked)}
          />
          <span>
            <span className="font-semibold">{label}</span>
            <span className="block text-xs text-muted">{tradeScenarioHints[idea.scenario]}</span>
          </span>
        </label>
        <span
          className={`shrink-0 rounded-full border px-2 py-0.5 text-xs ${goodRewardRisk ? 'border-line-strong text-up' : 'border-danger-border text-danger-text'}`}
        >
          R:R {formatMetric(idea.rewardRisk, 2)}
        </span>
      </header>
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
        <dt className="text-text-secondary">Цель</dt>
        <dd className="text-right text-up">{formatMetric(idea.target, 6)}</dd>
        <dt className="text-text-secondary">Вход, лимит</dt>
        <dd className="text-right">{formatMetric(idea.entry, 6)}</dd>
        <dt className="text-text-secondary">Стоп</dt>
        <dd className="text-right text-down">{formatMetric(idea.stop, 6)}</dd>
        <dt className="text-text-secondary">Сумма</dt>
        <dd className="text-right">{idea.amount ? `${formatMetric(idea.amount, 2)} USDT` : '—'}</dd>
        <dt className="text-text-secondary">Потеря на стопе</dt>
        <dd className="text-right">{idea.risk ? `${formatMetric(idea.risk, 2)} USDT` : '—'}</dd>
      </dl>
      <p className="text-xs text-muted">
        Лимитка ждёт {hours >= 1 ? `${hours} ч` : `${idea.entryValidityMinutes} мин`}, потом
        снимается.
      </p>
      {outcome && 'botId' in outcome ? (
        <p role="status" className="text-sm text-up">
          Создана —{' '}
          <Link
            to="/bots/$botId"
            params={{ botId: outcome.botId }}
            className="text-link hover:underline"
          >
            бот сделки
          </Link>
        </p>
      ) : outcome ? (
        <p role="alert" className="text-sm text-danger-text">
          {outcome.error}
        </p>
      ) : idea.taken ? (
        <p className="text-sm text-text-secondary">Такая сделка уже живёт.</p>
      ) : (
        <div className="mt-auto">
          <Button size="sm" variant="ghost" onClick={onHide} disabled={disabled}>
            Скрыть
          </Button>
        </div>
      )}
    </article>
  );
}

function key(idea: ScanTradeIdea): string {
  return `${idea.timeframe}:${idea.scenario}:${idea.entry}:${idea.stop}:${idea.target}`;
}

function total(
  ideas: readonly ScanTradeIdea[],
  value: (idea: ScanTradeIdea) => string | null,
): string {
  return ideas.reduce((sum, idea) => addDecimals(sum, value(idea) ?? '0') ?? sum, '0');
}
