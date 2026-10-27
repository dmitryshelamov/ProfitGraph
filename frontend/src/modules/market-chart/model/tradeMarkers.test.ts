import { drawableCandles } from './candleHistory';
import { markerPosition, tradeMarkers } from './tradeMarkers';

const hour = 3600;
const candle = (openTime: string, close: string) => ({
  openTime,
  open: close,
  high: close,
  low: close,
  close,
});
const trade = (id: string, time: string, side: 'Buy' | 'Sell', price: string) => ({
  id,
  time,
  side,
  price,
  text: price,
});

describe('trade markers', () => {
  const candles = drawableCandles([
    candle('2026-10-24T08:00:00Z', '1'),
    candle('2026-10-24T09:00:00Z', '2'),
    // 10:00 had no candle, as a Steam hour without sales or a closed session.
    candle('2026-10-24T11:00:00Z', '4'),
  ]);

  it('puts a trade on its candle at its own price and leaves out one no candle covers', () => {
    const markers = tradeMarkers(
      candles,
      [
        trade('b', '2026-10-24T11:30:00Z', 'Sell', '4.5'),
        trade('a', '2026-10-24T08:59:59Z', 'Buy', '1.25'),
        trade('gap', '2026-10-24T10:15:00Z', 'Buy', '2'),
        trade('early', '2026-10-24T07:00:00Z', 'Buy', '1'),
        trade('late', '2026-10-24T12:05:00Z', 'Buy', '5'),
      ],
      hour,
    );

    expect(markers.map((item) => [item.id, new Date(item.time * 1000).toISOString()])).toEqual([
      ['a', '2026-10-24T08:00:00.000Z'],
      ['b', '2026-10-24T11:00:00.000Z'],
    ]);
    expect(markers.map((item) => item.price)).toEqual([1.25, 4.5]);
  });

  it('draws a buy arrow below its candle and a sell arrow above it', () => {
    expect(markerPosition('Buy')).toBe('belowBar');
    expect(markerPosition('Sell')).toBe('aboveBar');
  });

  it('keeps every trade of a candle at its price and labels only the latest of a side once', () => {
    const markers = tradeMarkers(
      candles,
      [
        trade('2', '2026-10-24T09:20:00Z', 'Buy', '2.1'),
        trade('1', '2026-10-24T09:10:00Z', 'Buy', '2.2'),
        trade('s', '2026-10-24T09:30:00Z', 'Sell', '2.4'),
      ],
      hour,
    );

    expect(markers.map((item) => [item.id, item.price, item.text])).toEqual([
      ['1', 2.2, ''],
      ['2', 2.1, '2.1 ×2'],
      ['s', 2.4, '2.4'],
    ]);
  });
});
