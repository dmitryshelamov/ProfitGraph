import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';

import { ApiError } from '@/shared/api/client';
import { compareDecimals } from '@/shared/lib/decimal';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';

import { createScanTrade, scannerKeys } from '../api/scannerApi';
import { formatMetric } from '../model/format';
import { tradeScenarioHints, tradeScenarioLabels } from '../model/trades';
import type { ScanTradeIdea, ScanTradeIdeas } from '../model/types';

const createErrors: Record<string, string> = {
  scan_trade_live: 'По этой паре уже есть живая сделка.',
  scan_trade_entry_moved: 'Цена ушла от входа — карточки обновятся с новым снимком.',
  scan_trade_pair_not_scanned: 'Пары нет в последнем снимке сканера.',
  scan_trade_settings_missing: 'Сначала задайте риск и ключ.',
};

/**
 * The trade cards of a pair (ADR 0060): each proposes an entry, a stop and a target with the amount the owner's risk gives. «Создать»
 * creates a bot with real orders that buys at the entry, sells at the target and sells by market when a candle closes at the stop;
 * «Отмена» hides the card until the cards change.
 */
export function ScanTradeCards({ venue, ideas }: { venue: string; ideas: ScanTradeIdeas }) {
  const [hidden, setHidden] = useState<readonly string[]>([]);
  const visible = ideas.ideas.filter((idea) => !hidden.includes(key(idea)));

  if (ideas.ideas.length === 0) {
    return (
      <p className="text-sm text-muted">
        Идей нет: у пары не найдены уровни ниже цены и последний час не рос.
      </p>
    );
  }

  return (
    <div className="space-y-3">
      {ideas.hasLiveTrade && (
        <p role="status" className="text-sm text-warning-text">
          По паре уже есть живая сделка — новую можно создать после её завершения.
        </p>
      )}
      <div className="grid gap-3 md:grid-cols-3">
        {visible.map((idea) => (
          <TradeCard
            key={key(idea)}
            venue={venue}
            symbol={ideas.symbol}
            idea={idea}
            canCreate={ideas.hasSettings && !ideas.hasLiveTrade}
            onDismiss={() => setHidden((current) => [...current, key(idea)])}
          />
        ))}
      </div>
      {visible.length === 0 && (
        <p className="text-sm text-muted">Все карточки скрыты; они вернутся с новыми ценами.</p>
      )}
    </div>
  );
}

function TradeCard({
  venue,
  symbol,
  idea,
  canCreate,
  onDismiss,
}: {
  venue: string;
  symbol: string;
  idea: ScanTradeIdea;
  canCreate: boolean;
  onDismiss: () => void;
}) {
  const queryClient = useQueryClient();
  const create = useMutation({
    mutationFn: () => createScanTrade(venue, symbol, idea),
    onSettled: async () => {
      await queryClient.invalidateQueries({ queryKey: scannerKeys.tradeIdeas(venue, symbol) });
      await queryClient.invalidateQueries({ queryKey: scannerKeys.trades(venue) });
    },
  });
  const goodRewardRisk = (compareDecimals(idea.rewardRisk, '2') ?? -1) >= 0;
  const hours = idea.entryValidityMinutes / 60;

  return (
    <article
      aria-label={`Идея: ${tradeScenarioLabels[idea.scenario] ?? idea.scenario}`}
      className="flex flex-col gap-3 rounded-lg border border-line-strong bg-panel-2 p-4"
    >
      <header className="flex items-start justify-between gap-2">
        <div>
          <h3 className="font-semibold">{tradeScenarioLabels[idea.scenario] ?? idea.scenario}</h3>
          <p className="text-xs text-muted">{tradeScenarioHints[idea.scenario]}</p>
        </div>
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
        снимается. Стоп проверяется на закрытии минутной свечи.
      </p>
      {create.isSuccess ? (
        <p role="status" className="text-sm text-up">
          Сделка создана —{' '}
          <Link
            to="/bots/$botId"
            params={{ botId: create.data.botId }}
            className="text-link hover:underline"
          >
            бот сделки
          </Link>
        </p>
      ) : (
        <div className="mt-auto flex gap-2">
          <Button
            variant="primary"
            size="sm"
            disabled={!canCreate || !idea.amount || create.isPending}
            onClick={() => create.mutate()}
          >
            {create.isPending ? 'Создаю…' : 'Создать'}
          </Button>
          <Button size="sm" variant="ghost" onClick={onDismiss} disabled={create.isPending}>
            Отмена
          </Button>
        </div>
      )}
      {create.isError &&
        (create.error instanceof ApiError && createErrors[create.error.code] ? (
          <p role="alert" className="text-sm text-danger-text">
            {createErrors[create.error.code]}
          </p>
        ) : (
          <ApiErrorMessage error={create.error} fallback="Не удалось создать сделку." />
        ))}
    </article>
  );
}

function key(idea: ScanTradeIdea): string {
  return `${idea.scenario}:${idea.entry}:${idea.stop}:${idea.target}`;
}
