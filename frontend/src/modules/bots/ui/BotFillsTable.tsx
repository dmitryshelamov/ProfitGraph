import { useInfiniteQuery } from '@tanstack/react-query';

import { Button } from '@/shared/ui/Button';
import { DateTime } from '@/shared/ui/DateTime';

import { botFillsQuery } from '../api/botsApi';
import { atPlaces, type BotDisplay, withAsset } from '../model/botDisplay';

function feeText(fee: string, inBase: boolean, display: BotDisplay): string {
  const places = inBase ? display.basePlaces : display.quotePlaces;
  const asset = inBase ? display.baseAsset : display.quoteAsset;

  return withAsset(atPlaces(fee, places), asset ?? (inBase ? '(база)' : '(котировка)'));
}

export function BotFillsTable({ botId, display }: { botId: string; display: BotDisplay }) {
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
            <th className="py-2">Количество{display.baseAsset ? `, ${display.baseAsset}` : ''}</th>
            <th className="py-2">Цена{display.quoteAsset ? `, ${display.quoteAsset}` : ''}</th>
            <th className="py-2">Сумма{display.quoteAsset ? `, ${display.quoteAsset}` : ''}</th>
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
              <td className="py-2 font-mono">{atPlaces(fill.grossBase, display.basePlaces)}</td>
              <td className="py-2 font-mono">{atPlaces(fill.price, display.pricePlaces)}</td>
              <td className="py-2 font-mono">{atPlaces(fill.grossQuote, display.quotePlaces)}</td>
              <td className="py-2 font-mono">
                {fill.commissionPending ? (
                  <span className="font-sans text-muted">комиссия ещё не пришла</span>
                ) : (
                  <>{feeText(fill.fee, fill.feeAsset === 'Base', display)}</>
                )}
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
