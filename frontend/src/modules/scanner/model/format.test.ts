import { formatMetric } from './format';

describe('formatMetric', () => {
  it('rounds to the places and groups thousands', () => {
    expect(formatMetric('1234567.891', 0)).toBe('1 234 568');
    expect(formatMetric('-2.3456', 2)).toBe('-2.35');
    expect(formatMetric('0.00012345', null)).toBe('0.00012345');
    expect(formatMetric(null, 2)).toBe('—');
  });
});
