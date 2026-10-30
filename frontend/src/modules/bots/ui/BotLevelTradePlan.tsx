import { atPlaces, type BotDisplay, withAsset } from '../model/botDisplay';
import { type LevelTrade, percentFrom, plannedOutcome, stopRule } from '../model/levelTrade';

/** A level's distance in words: "−4.76% от текущей · −5% от средней", or nothing without a price to measure from. */
function distances(level: string, price: string | null, average: string | null): string | null {
  const parts = [
    price === null ? null : percentFrom(level, price),
    average === null ? null : percentFrom(level, average),
  ];
  const words = [
    parts[0] ? `${parts[0]} от текущей` : null,
    parts[1] ? `${parts[1]} от средней` : null,
  ].filter((part) => part !== null);

  return words.length === 0 ? null : words.join(' · ');
}

/**
 * The prices a level trade (a market scanner's trade card) exits at, from the bot's parameters: the target and the entry are
 * limit orders on the venue, while the stop is no order at all: the bot exits at market once a candle of its own timeframe
 * closes at or past it, so it may fill worse than the stop. Each price shows how far it is from the current price and, with a
 * position, from its average price; the planned loss and profit assume the whole amount entered at the entry price.
 */
export function BotLevelTradePlan({
  trade,
  timeframe,
  display,
  price,
  average,
}: {
  trade: LevelTrade;
  timeframe: string;
  display: BotDisplay;
  price: string | null;
  average: string | null;
}) {
  const { quoteAsset } = display;
  const priceText = (value: string) => withAsset(atPlaces(value, display.pricePlaces), quoteAsset);
  const quote = (value: string) => withAsset(atPlaces(value, display.quotePlaces), quoteAsset);
  const outcome = plannedOutcome(trade);
  const exit = trade.direction === 'Long' ? 'продажа' : 'покупка';
  const rows = [
    {
      id: 'target',
      label: 'Цель',
      value: trade.target,
      note: `лимитная ${exit} на бирже`,
      tone: 'text-up',
    },
    {
      id: 'entry',
      label: 'Вход',
      value: trade.entry,
      note: `лимитная ${trade.direction === 'Long' ? 'покупка' : 'продажа'} на бирже`,
      tone: '',
    },
    {
      id: 'stop',
      label: 'Стоп',
      value: trade.stop,
      note: `не заявка на бирже: ${stopRule(trade, timeframe).toLowerCase()}`,
      tone: 'text-down',
    },
  ];

  return (
    <section aria-labelledby="level-trade-plan" className="space-y-3">
      <h3 id="level-trade-plan" className="flex flex-wrap gap-x-2 text-sm font-medium">
        План сделки
        <span className="font-normal text-muted">
          {trade.direction === 'Long' ? 'лонг' : 'шорт'} на {quote(trade.amount)}
        </span>
      </h3>
      <dl className="divide-y divide-line rounded-md border border-line bg-panel-2">
        {rows.map((row) => {
          const away = distances(row.value, price, average);

          return (
            <div key={row.id} className="flex flex-wrap items-baseline gap-x-3 px-4 py-2">
              <dt className="w-12 text-xs text-muted">{row.label}</dt>
              <dd className={`font-mono ${row.tone}`.trim()} aria-label={`${row.label} сделки`}>
                {priceText(row.value)}
              </dd>
              {away && <dd className="font-mono text-xs text-text-secondary">{away}</dd>}
              <dd className="basis-full pl-15 text-xs text-muted">{row.note}</dd>
            </div>
          );
        })}
      </dl>
      {outcome && (
        <p className="text-xs text-muted">
          На стопе ≈ <span className="font-mono text-down">−{quote(outcome.loss)}</span>, на цели ≈{' '}
          <span className="font-mono text-up">+{quote(outcome.profit)}</span>, если вход исполнится
          целиком; без комиссий, а стоп по рынку может исполниться хуже.
        </p>
      )}
    </section>
  );
}
