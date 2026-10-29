import { formatMetric, withQuote } from './format';

describe('formatMetric', () => {
  it('rounds to the places and groups thousands', () => {
    expect(formatMetric('1234567.891', 0)).toBe('1 234 568');
    expect(formatMetric('-2.3456', 2)).toBe('-2.35');
    expect(formatMetric('0.00012345', null)).toBe('0.00012345');
    expect(formatMetric(null, 2)).toBe('—');
  });
});

describe('withQuote', () => {
  it("follows the value with the venue's currency, or falls back without one", () => {
    expect(withQuote('1 250.00', 'RUB')).toBe('1 250.00 RUB');
    expect(withQuote('Риск на сделку,', 'USDT', 'Риск на сделку')).toBe('Риск на сделку, USDT');
    expect(withQuote('Риск на сделку,', '', 'Риск на сделку')).toBe('Риск на сделку');
    expect(withQuote('12.50', '')).toBe('12.50');
  });
});
