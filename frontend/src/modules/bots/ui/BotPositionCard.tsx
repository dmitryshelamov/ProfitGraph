import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';

import { multiplyDecimals, subtractDecimals } from '@/shared/lib/decimal';
import { DateTime } from '@/shared/ui/DateTime';

import { botPositionQuery } from '../api/botsApi';
import { atPlaces, type BotDisplay, withAsset } from '../model/botDisplay';
import type { LivePrice } from '../model/types';
import { followLivePrice } from '../realtime/followLivePrice';

function ageLabel(observedAt: string, now: number): string {
  const seconds = Math.max(0, Math.round((now - Date.parse(observedAt)) / 1000));

  return seconds < 60 ? `${seconds} с назад` : `${Math.round(seconds / 60)} мин назад`;
}

/**
 * The bot's position at its display price: a fresh ticker from /ws/live-prices when one arrived after the stored
 * value, otherwise what the server chose (a fresh ticker or the last candle's close). With a pushed ticker the
 * unrealized profit is recomputed here as the server does, base total × price − cost basis, in exact decimal strings.
 */
export function BotPositionCard({ botId, display }: { botId: string; display: BotDisplay }) {
  const position = useQuery(botPositionQuery(botId));
  const [live, setLive] = useState<LivePrice | null>(null);
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => followLivePrice(botId, setLive), [botId]);
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000);

    return () => window.clearInterval(timer);
  }, []);

  if (position.isError) {
    return (
      <p role="alert" className="text-danger-text">
        Не удалось загрузить позицию.
      </p>
    );
  }

  if (!position.data) {
    return <p className="text-muted">Загрузка…</p>;
  }

  const data = position.data;
  const fromLive =
    live !== null &&
    (data.markPriceObservedAt === null ||
      Date.parse(live.observedAt) > Date.parse(data.markPriceObservedAt));
  const price = fromLive ? live.price : data.markPrice;
  const observedAt = fromLive ? live.observedAt : data.markPriceObservedAt;
  const source =
    fromLive || data.markPriceSource === 'Ticker' ? 'тикер биржи' : 'закрытие последней свечи';
  const unrealizedPnl = fromLive
    ? subtractDecimals(multiplyDecimals(data.baseTotal, live.price) ?? '', data.costBasis)
    : data.unrealizedPnl;
  const { baseAsset, quoteAsset } = display;
  const base = (value: string) => withAsset(atPlaces(value, display.basePlaces), baseAsset);
  const quote = (value: string) => withAsset(atPlaces(value, display.quotePlaces), quoteAsset);
  const priceText = (value: string) => withAsset(atPlaces(value, display.pricePlaces), quoteAsset);

  return (
    <div className="space-y-4">
      <dl className="grid grid-cols-2 gap-x-6 gap-y-2 text-sm md:grid-cols-3">
        <dt className="text-text-secondary">Базовый актив</dt>
        <dd className="font-mono">{base(data.baseTotal)}</dd>
        <dt className="text-text-secondary">Котируемый актив</dt>
        <dd className="font-mono">{quote(data.quoteTotal)}</dd>
        <dt className="text-text-secondary">Себестоимость</dt>
        <dd className="font-mono">{quote(data.costBasis)}</dd>
        <dt className="text-text-secondary">Средняя цена</dt>
        <dd className="font-mono">
          {data.averageCost === null ? '—' : priceText(data.averageCost)}
        </dd>
        <dt className="text-text-secondary">Реализованная прибыль</dt>
        <dd className="font-mono">{quote(data.realizedPnl)}</dd>
        <dt className="text-text-secondary">Нереализованная прибыль</dt>
        <dd className="font-mono" aria-label="Нереализованная прибыль">
          {unrealizedPnl === null ? '—' : quote(unrealizedPnl)}
        </dd>
        <dt className="text-text-secondary">Цена</dt>
        <dd className="font-mono" aria-label="Цена для отображения">
          {price === null ? '—' : priceText(price)}
        </dd>
        <dt className="text-text-secondary">Источник цены</dt>
        <dd>{price ? `${source}, ${observedAt ? ageLabel(observedAt, now) : ''}` : '—'}</dd>
        <dt className="text-text-secondary">Позиция открыта</dt>
        <dd>{data.openedAt ? <DateTime value={data.openedAt} /> : '—'}</dd>
      </dl>
      {data.cycles.length > 0 && (
        <table className="w-full text-left text-sm" aria-label="Циклы позиции">
          <thead className="text-text-secondary">
            <tr>
              <th className="py-2">Открыта</th>
              <th className="py-2">Закрыта</th>
              <th className="py-2">Прибыль</th>
            </tr>
          </thead>
          <tbody>
            {data.cycles.map((cycle) => (
              <tr key={cycle.id} className="border-t border-line">
                <td className="py-2">
                  <DateTime value={cycle.openedAt} />
                </td>
                <td className="py-2">
                  {cycle.closedAt ? <DateTime value={cycle.closedAt} /> : 'открыта'}
                </td>
                <td className="py-2 font-mono">
                  {cycle.realizedPnl === null ? '—' : quote(cycle.realizedPnl)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
