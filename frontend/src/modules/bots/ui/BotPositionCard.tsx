import { useQuery } from '@tanstack/react-query';
import { type ReactNode, useEffect, useState } from 'react';

import {
  compareDecimals,
  divideDecimals,
  multiplyDecimals,
  subtractDecimals,
} from '@/shared/lib/decimal';
import { DateTime } from '@/shared/ui/DateTime';

import { botPositionQuery } from '../api/botsApi';
import { atPlaces, type BotDisplay, exactAmount, withAsset } from '../model/botDisplay';
import { remainderReason, unsellableRemainder } from '../model/positionRemainder';
import type { LivePrice } from '../model/types';
import { followLivePrice } from '../realtime/followLivePrice';

function ageLabel(observedAt: string, now: number): string {
  const seconds = Math.max(0, Math.round((now - Date.parse(observedAt)) / 1000));

  return seconds < 60 ? `${seconds} с назад` : `${Math.round(seconds / 60)} мин назад`;
}

/** A profit's sign: 1 above zero, −1 below, 0 at zero or for a value that is not a decimal. */
function sign(value: string): number {
  return compareDecimals(value, '0') ?? 0;
}

function toneClass(value: string): string {
  const direction = sign(value);

  return direction > 0 ? 'text-up' : direction < 0 ? 'text-down' : '';
}

/** A profit with its sign: "+55.3 RUB", "−12 RUB", "0 RUB". */
function signed(text: string, value: string): string {
  return sign(value) > 0 ? `+${text}` : text.replace(/^-/, '−');
}

/** The profit as a percent of what the position cost, rounded down to hundredths; null for a zero cost. */
function percentOfCost(profit: string, cost: string): string | null {
  const direction = sign(profit);
  const magnitude = direction < 0 ? profit.slice(1) : profit;
  const percent = divideDecimals(multiplyDecimals(magnitude, '100') ?? '', cost, 2);

  if (percent === null || compareDecimals(cost, '0') !== 1) {
    return null;
  }

  return direction > 0 ? `+${percent}%` : direction < 0 ? `−${percent}%` : `${percent}%`;
}

function Metric({
  label,
  value,
  caption,
  valueLabel,
  valueClass = '',
}: {
  label: string;
  value: ReactNode;
  caption?: ReactNode;
  valueLabel?: string;
  valueClass?: string;
}) {
  return (
    <div className="rounded-md border border-line bg-panel-2 px-4 py-3">
      <dt className="text-xs text-muted">{label}</dt>
      <dd className={`mt-1 font-mono text-lg ${valueClass}`.trim()} aria-label={valueLabel}>
        {value}
      </dd>
      {caption && <dd className="mt-1 text-xs text-muted">{caption}</dd>}
    </div>
  );
}

/**
 * The bot's position at its display price: a fresh ticker from /ws/live-prices when one arrived after the stored
 * value, otherwise what the server chose (a fresh ticker or the last candle's close). With a pushed ticker the
 * unrealized profit is recomputed here as the server does, base total × price − cost basis, in exact decimal strings.
 * Entry fees count as spent when they were paid (ADR 0063): the realized profit includes them, while the unrealized profit and
 * the average buy price leave them out and the break-even price keeps them. Realized profit shows a dash until there is any:
 * neither a closed cycle nor a non-zero realized total. The metrics
 * take as many columns as the card's own width allows; the position's cycles are shown by {@link BotPositionCycles}.
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
  // What the held base cost without the entry fees, which the profit counts as spent when they were paid (ADR 0063).
  const boughtFor = subtractDecimals(data.costBasis, data.entryFees) ?? data.costBasis;
  const unrealizedPnl = fromLive
    ? subtractDecimals(multiplyDecimals(data.baseTotal, live.price) ?? '', boughtFor)
    : data.unrealizedPnl;
  const remainder = unsellableRemainder(data.baseTotal, display, price);
  const isOpen = sign(data.baseTotal) > 0 && remainder === null;
  const hasRealized = sign(data.realizedPnl) !== 0 || data.cycles.some((cycle) => cycle.closedAt);
  const hasEntryFees = sign(data.entryFees) > 0;
  const { baseAsset, quoteAsset } = display;
  // A quantity is never rounded up: a remainder rounded to the step would look sellable.
  const base = (value: string) => withAsset(exactAmount(value), baseAsset);
  const quote = (value: string) => withAsset(atPlaces(value, display.quotePlaces), quoteAsset);
  const priceText = (value: string) => withAsset(atPlaces(value, display.pricePlaces), quoteAsset);
  const profit = (value: string) => signed(quote(value), value);
  const unrealizedPercent =
    isOpen && unrealizedPnl !== null ? percentOfCost(unrealizedPnl, boughtFor) : null;

  return (
    <div className="@container space-y-5">
      <p className="flex flex-wrap items-center gap-2 text-sm">
        <span
          aria-hidden="true"
          className={`size-2 rounded-full ${isOpen ? 'bg-up' : 'bg-muted'}`}
        />
        {isOpen ? (
          <>
            <span className="font-medium">Позиция открыта</span>
            {data.openedAt && (
              <span className="text-muted">
                с <DateTime value={data.openedAt} />
              </span>
            )}
          </>
        ) : remainder ? (
          <span className="text-muted">
            Позиции нет · остаток {base(data.baseTotal)} —{' '}
            {remainderReason(remainder, baseAsset, quoteAsset)}
          </span>
        ) : (
          <span className="text-muted">Позиции нет</span>
        )}
      </p>
      <dl className="grid grid-cols-1 gap-3 @2xs:grid-cols-2 @xl:grid-cols-3">
        <Metric
          label="Объём"
          value={base(data.baseTotal)}
          caption={isOpen ? `куплено на ${quote(boughtFor)}` : undefined}
        />
        <Metric
          label="Средняя цена покупки"
          valueLabel="Средняя цена покупки"
          value={!isOpen || data.averageBuyPrice === null ? '—' : priceText(data.averageBuyPrice)}
          caption={
            isOpen && hasEntryFees && data.averageCost !== null
              ? `безубыток ${priceText(data.averageCost)}`
              : undefined
          }
        />
        <Metric
          label="Текущая цена"
          valueLabel="Цена для отображения"
          value={price === null ? '—' : priceText(price)}
          caption={
            price === null
              ? undefined
              : observedAt
                ? `${source} · ${ageLabel(observedAt, now)}`
                : source
          }
        />
        <Metric
          label="Нереализованная прибыль"
          valueLabel="Нереализованная прибыль"
          value={!isOpen || unrealizedPnl === null ? '—' : profit(unrealizedPnl)}
          valueClass={!isOpen || unrealizedPnl === null ? '' : toneClass(unrealizedPnl)}
          caption={unrealizedPercent ? `${unrealizedPercent} к цене покупки` : undefined}
        />
        <Metric
          label="Реализованная прибыль"
          valueLabel="Реализованная прибыль"
          value={hasRealized ? profit(data.realizedPnl) : '—'}
          valueClass={hasRealized ? toneClass(data.realizedPnl) : ''}
          caption={
            hasEntryFees
              ? 'с комиссиями покупок'
              : hasRealized
                ? 'по проданному объёму'
                : 'продаж ещё не было'
          }
        />
        <Metric
          label="Комиссии позиции"
          valueLabel="Комиссии позиции"
          value={isOpen && data.positionFees !== null ? quote(data.positionFees) : '—'}
          caption={isOpen ? 'все сделки открытой позиции' : undefined}
        />
        <Metric
          label="Свободные средства"
          value={quote(data.quoteTotal)}
          caption="не вложены в позицию"
        />
      </dl>
    </div>
  );
}

/** The position's latest cycles, from buying in to selling out, with their fees in quote and each closed cycle's realized profit. */
export function BotPositionCycles({ botId, display }: { botId: string; display: BotDisplay }) {
  const position = useQuery(botPositionQuery(botId));
  const data = position.data;

  if (!data || data.cycles.length === 0) {
    return null;
  }

  const quote = (value: string) =>
    withAsset(atPlaces(value, display.quotePlaces), display.quoteAsset);
  const profit = (value: string) => signed(quote(value), value);

  return (
    <div>
      <h3 className="mb-2 text-sm font-medium text-text-secondary">Циклы позиции</h3>
      <table className="w-full text-left text-sm" aria-label="Циклы позиции">
        <thead className="text-xs text-muted">
          <tr>
            <th className="py-2 font-normal">Открыт</th>
            <th className="py-2 font-normal">Закрыт</th>
            <th className="py-2 font-normal">Комиссии</th>
            <th className="py-2 text-right font-normal">Прибыль</th>
          </tr>
        </thead>
        <tbody>
          {data.cycles.map((cycle) => (
            <tr key={cycle.id} className="border-t border-line">
              <td className="py-2">
                <DateTime value={cycle.openedAt} />
              </td>
              <td className="py-2">
                {cycle.closedAt ? (
                  <DateTime value={cycle.closedAt} />
                ) : (
                  <span className="text-up">открыт</span>
                )}
              </td>
              <td className="py-2 font-mono">{quote(cycle.fees)}</td>
              <td
                className={`py-2 text-right font-mono ${cycle.realizedPnl === null ? 'text-muted' : toneClass(cycle.realizedPnl)}`}
              >
                {cycle.realizedPnl === null ? '—' : profit(cycle.realizedPnl)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
