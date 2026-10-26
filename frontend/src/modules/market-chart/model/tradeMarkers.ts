import { type ChartTrade, seconds } from './candleHistory';
import type { DrawnCandle } from './pricePoint';

/** A trade drawn at its own price on the candle that was open when it happened. */
export interface TradeMarker {
  readonly id: string;
  readonly time: number;
  readonly side: 'Buy' | 'Sell';
  readonly price: number;
  readonly text: string;
}

/** Where the marker sits against its price: a buy arrow points up from below the price, a sell arrow down from above it. */
export function markerPosition(side: 'Buy' | 'Sell'): 'atPriceBottom' | 'atPriceTop' {
  return side === 'Buy' ? 'atPriceBottom' : 'atPriceTop';
}

function candleAt(candles: readonly DrawnCandle[], at: number): DrawnCandle | undefined {
  let low = 0;
  let high = candles.length - 1;
  let found: DrawnCandle | undefined;

  while (low <= high) {
    const middle = (low + high) >> 1;
    const candle = candles[middle];

    if (candle && candle.time <= at) {
      found = candle;
      low = middle + 1;
    } else {
      high = middle - 1;
    }
  }

  return found;
}

/**
 * Places each trade on the candle whose period contains it, at the trade's own price, in time order as the chart library
 * requires. A trade outside every drawn candle is left out rather than pinned to a neighbour that does not cover its
 * price: one before the first candle (until older ones load), in a gap (a closed session or a Steam hour without sales)
 * or after the newest closed candle.
 *
 * Several trades of one side on one candle each keep their price; only the latest carries the label, with their count,
 * so the labels do not pile up.
 */
export function tradeMarkers(
  candles: readonly DrawnCandle[],
  trades: readonly ChartTrade[],
  candleSeconds: number,
): TradeMarker[] {
  const placed: (TradeMarker & { readonly at: number })[] = [];

  for (const trade of trades) {
    const at = seconds(trade.time);
    const candle = candleAt(candles, at);

    if (candle && at < candle.time + candleSeconds) {
      placed.push({
        id: trade.id,
        time: candle.time,
        side: trade.side,
        price: Number(trade.price),
        text: trade.text,
        at,
      });
    }
  }

  placed.sort((left, right) => left.time - right.time || left.at - right.at);

  const groups = new Map<string, number[]>();
  placed.forEach((marker, index) => {
    const key = `${marker.time}:${marker.side}`;
    groups.set(key, [...(groups.get(key) ?? []), index]);
  });

  return placed.map((marker, index) => {
    const group = groups.get(`${marker.time}:${marker.side}`) ?? [];
    const plain: TradeMarker = {
      id: marker.id,
      time: marker.time,
      side: marker.side,
      price: marker.price,
      text: marker.text,
    };

    if (group.length === 1) {
      return plain;
    }

    return index === group[group.length - 1]
      ? { ...plain, text: `${marker.text} ×${group.length}` }
      : { ...plain, text: '' };
  });
}
