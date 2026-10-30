import { botChartTimeframe, chartTimeframesFor, shownTimeframe } from './chartTimeframes';

describe('bot chart timeframes', () => {
  it('offers the chart’s set with the bot’s own timeframe in order', () => {
    expect(chartTimeframesFor('1h')).toEqual(['1m', '5m', '15m', '1h', '4h', '1d']);
    expect(chartTimeframesFor('30m')).toEqual(['1m', '5m', '15m', '30m', '1h', '4h', '1d']);
  });

  it('keeps only what the venue offers, and always the bot’s own timeframe', () => {
    expect(chartTimeframesFor('1h', ['1h', '1d'])).toEqual(['1h', '1d']);
    expect(chartTimeframesFor('1h', ['1m', '3m', '1d'])).toEqual(['1m', '1h', '1d']);
  });

  it('reads a known timeframe from the address and shows the bot’s own otherwise', () => {
    expect(botChartTimeframe('4h')).toBe('4h');
    expect(botChartTimeframe('7m')).toBeUndefined();
    expect(botChartTimeframe(4)).toBeUndefined();
    expect(shownTimeframe('4h', '1h', ['1h', '4h'])).toBe('4h');
    expect(shownTimeframe('4h', '1h', ['1h', '1d'])).toBe('1h');
    expect(shownTimeframe(undefined, '1h', ['1h'])).toBe('1h');
  });
});
