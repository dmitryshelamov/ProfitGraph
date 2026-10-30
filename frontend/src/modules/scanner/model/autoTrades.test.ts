import { overall } from './autoTrades';

describe('auto trades', () => {
  it('weighs the thirds by their trades', () => {
    expect(
      overall([
        { trades: 40, meanR: '0.1' },
        { trades: 60, meanR: '0.2' },
        { trades: 0, meanR: null },
      ]),
    ).toEqual({ trades: 100, meanR: '0.16' });
    expect(overall([{ trades: 0, meanR: null }])).toEqual({ trades: 0, meanR: null });
  });
});
