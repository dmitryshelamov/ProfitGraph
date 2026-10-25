import { useInfiniteQuery } from '@tanstack/react-query';

import { Button } from '@/shared/ui/Button';
import { DateTime } from '@/shared/ui/DateTime';

import { botFillsQuery } from '../api/botsApi';

export function BotFillsTable({ botId }: { botId: string }) {
  const fills = useInfiniteQuery(botFillsQuery(botId));
  const items = fills.data?.pages.flatMap((page) => page.items) ?? [];

  if (fills.isError) {
    return (
      <p role="alert" className="text-danger-text">
        Не удалось загрузить сделки.
      </p>
    );
  }

  if (items.length === 0) {
    return <p className="text-muted">{fills.isPending ? 'Загрузка…' : 'Сделок пока нет.'}</p>;
  }

  return (
    <>
      <table className="w-full text-left text-sm" aria-label="Сделки бота">
        <thead className="text-text-secondary">
          <tr>
            <th className="py-2">Время</th>
            <th className="py-2">Сторона</th>
            <th className="py-2">Количество</th>
            <th className="py-2">Цена</th>
            <th className="py-2">Сумма</th>
            <th className="py-2">Комиссия</th>
          </tr>
        </thead>
        <tbody>
          {items.map((fill) => (
            <tr key={fill.id} className="border-t border-line">
              <td className="py-2">
                <DateTime value={fill.filledAt} />
              </td>
              <td className="py-2">{fill.side === 'Buy' ? 'Покупка' : 'Продажа'}</td>
              <td className="py-2 font-mono">{fill.grossBase}</td>
              <td className="py-2 font-mono">{fill.price}</td>
              <td className="py-2 font-mono">{fill.grossQuote}</td>
              <td className="py-2 font-mono">
                {fill.fee} {fill.feeAsset === 'Base' ? '(база)' : '(котировка)'}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {fills.hasNextPage && (
        <Button
          size="sm"
          className="mt-3"
          disabled={fills.isFetchingNextPage}
          onClick={() => void fills.fetchNextPage()}
        >
          Показать ещё сделки
        </Button>
      )}
    </>
  );
}
