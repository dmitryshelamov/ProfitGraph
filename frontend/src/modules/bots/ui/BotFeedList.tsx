import { useInfiniteQuery } from '@tanstack/react-query';

import { divideDecimals } from '@/shared/lib/decimal';
import { Button } from '@/shared/ui/Button';
import { DateTime } from '@/shared/ui/DateTime';

import { botFeedQuery } from '../api/botsApi';
import { type BotDisplay, feeLabel, unknownBotDisplay } from '../model/botDisplay';
import { feedLabel, feedReasonLabel } from '../model/botStatus';
import { absenceProofText, autoCheckFailure } from '../model/orderAbsence';
import { waitingReason } from '../model/orderWaiting';
import { closeFailureLabel } from '../model/positionClose';
import type { BotFeedEntry } from '../model/types';

const settingLabels: Record<string, string> = {
  feeRates: 'ставки комиссий',
  parameters: 'параметры стратегии',
  riskLimits: 'лимиты риска',
  foreignOrders: 'общий символ',
};

/** A fill's fee after its amounts, in quote first; nothing for a fee not known yet, as a commission that has not arrived. */
function fillFee(entry: BotFeedEntry, display: BotDisplay): string {
  const { fee, feeAsset, grossBase, grossQuote } = entry.details;

  if (!fee || Number(fee) === 0 || !grossBase || !grossQuote) {
    return '';
  }

  const price = divideDecimals(grossQuote, grossBase, 12) ?? '0';

  return ` · комиссия ${feeLabel(fee, feeAsset === 'Quote' ? 'Quote' : 'Base', price, display)}`;
}

function details(entry: BotFeedEntry, display: BotDisplay): string {
  const { reason, side, grossBase, grossQuote, realizedPnl, engineOrderId, setting } =
    entry.details;

  if (entry.kind === 'PositionCloseStarted') {
    return `ордеров к отмене: ${entry.details.cancelledOrders ?? '0'}; ${entry.details.stopAfter === 'true' ? 'после закрытия бот остановится' : 'бот продолжит работу'}`;
  }

  if (entry.kind === 'PositionCloseEnded') {
    return entry.details.outcome === 'Closed'
      ? 'позиция закрыта'
      : `не удалось: ${closeFailureLabel(entry.details.failure ?? null)}`;
  }

  if (setting) {
    const label = settingLabels[setting] ?? setting;

    if (setting === 'foreignOrders') {
      return `${label} ${entry.details.allowForeignOrders === 'true' ? 'включён' : 'выключен'}`;
    }

    const { added, removed } = entry.details;
    const changes = [
      ...(added ? [`по умолчанию ${added}`] : []),
      ...(removed ? [`удалены ${removed}`] : []),
    ];

    return changes.length > 0 ? `${label}: ${changes.join('; ')}` : label;
  }

  if (entry.details.checks) {
    return `заявка ${engineOrderId} закрыта автоматически, потому что ${absenceProofText(entry.details.checks)}`;
  }

  if (reason) {
    const label = feedReasonLabel(entry.kind, reason);
    const unclosed = autoCheckFailure(entry.details.detail);

    if (unclosed) {
      return `${label}; автоматически заявку закрыть не удалось: ${unclosed}`;
    }

    return entry.details.detail ? `${label} (${entry.details.detail})` : label;
  }

  if (entry.details.restatedClose) {
    return `свеча ${entry.details.openTime}: закрытие было ${entry.details.previousClose}, стало ${entry.details.restatedClose}`;
  }

  if (entry.details.correctedFee) {
    return `заявка ${engineOrderId}: было ${entry.details.previousFee}, стало ${entry.details.correctedFee}`;
  }

  if (entry.details.code) {
    return `заявка ${engineOrderId}: ${waitingReason(entry.details.code).toLowerCase()}`;
  }

  if (grossBase && grossQuote) {
    return `${side === 'Sell' ? 'продажа' : 'покупка'} ${grossBase} на ${grossQuote}${fillFee(entry, display)}`;
  }

  if (realizedPnl) {
    return `прибыль ${realizedPnl}`;
  }

  return engineOrderId ? `заявка ${engineOrderId}` : '';
}

/** What happened to the bot, newest first; entries are kept for 90 days. */
export function BotFeedList({
  botId,
  display = unknownBotDisplay,
}: {
  botId: string;
  display?: BotDisplay;
}) {
  const feed = useInfiniteQuery(botFeedQuery(botId));
  const items = feed.data?.pages.flatMap((page) => page.items) ?? [];

  if (feed.isError) {
    return (
      <p role="alert" className="text-danger-text">
        Не удалось загрузить ленту.
      </p>
    );
  }

  if (items.length === 0) {
    return <p className="text-muted">{feed.isPending ? 'Загрузка…' : 'Событий пока нет.'}</p>;
  }

  return (
    <>
      <ul className="space-y-1 text-sm" aria-label="Лента бота">
        {items.map((entry) => {
          const text = details(entry, display);

          return (
            <li key={entry.id} className="flex gap-3 border-t border-line pt-1">
              <span className="text-muted">
                <DateTime value={entry.occurredAt} />
              </span>
              <span>
                {feedLabel(entry.kind)}
                {text && `: ${text}`}
              </span>
            </li>
          );
        })}
      </ul>
      {feed.hasNextPage && (
        <Button
          size="sm"
          className="mt-3"
          disabled={feed.isFetchingNextPage}
          onClick={() => void feed.fetchNextPage()}
        >
          Показать ещё события
        </Button>
      )}
    </>
  );
}
