import { useInfiniteQuery } from '@tanstack/react-query';

import { Button } from '@/shared/ui/Button';
import { DateTime } from '@/shared/ui/DateTime';

import { botFeedQuery } from '../api/botsApi';
import { feedLabel, reasonLabel } from '../model/botStatus';
import type { BotFeedEntry } from '../model/types';

const settingLabels: Record<string, string> = {
  feeRates: 'ставки комиссий',
  parameters: 'параметры стратегии',
  riskLimits: 'лимиты риска',
  foreignOrders: 'общий символ',
};

function details(entry: BotFeedEntry): string {
  const { reason, side, grossBase, grossQuote, realizedPnl, engineOrderId, setting } =
    entry.details;

  if (setting) {
    const label = settingLabels[setting] ?? setting;

    return setting === 'foreignOrders'
      ? `${label} ${entry.details.allowForeignOrders === 'true' ? 'включён' : 'выключен'}`
      : label;
  }

  if (reason) {
    return reasonLabel(reason);
  }

  if (entry.details.correctedFee) {
    return `заявка ${engineOrderId}: было ${entry.details.previousFee}, стало ${entry.details.correctedFee}`;
  }

  if (grossBase && grossQuote) {
    return `${side === 'Sell' ? 'продажа' : 'покупка'} ${grossBase} на ${grossQuote}`;
  }

  if (realizedPnl) {
    return `прибыль ${realizedPnl}`;
  }

  return engineOrderId ? `заявка ${engineOrderId}` : '';
}

/** What happened to the bot, newest first; entries are kept for 90 days. */
export function BotFeedList({ botId }: { botId: string }) {
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
          const text = details(entry);

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
