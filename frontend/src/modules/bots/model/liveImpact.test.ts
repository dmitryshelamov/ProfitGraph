import { describe, expect, it } from 'vitest';

import { parameterFit, parameterFitText } from './liveImpact';
import type { LiveImpactBot } from './types';

const bot: LiveImpactBot = {
  botId: 'b1',
  venue: 'bybit',
  symbol: 'BTCUSDT',
  timeframe: '1h',
  strategyVersionId: 'v1',
  runtimeStatus: 'Running',
  parameters: { drop: '0.05', rise: '0.1' },
};

describe('parameterFit', () => {
  it('gives a new parameter its default and drops a removed one', () => {
    expect(
      parameterFit(bot, [
        { name: 'drop', defaultValue: '0.03' },
        { name: 'buyAmount', defaultValue: '50' },
      ]),
    ).toEqual({ added: [{ name: 'buyAmount', defaultValue: '50' }], removed: ['rise'] });
  });

  it('describes only the bots whose values change', () => {
    const same = [
      { name: 'drop', defaultValue: '0.03' },
      { name: 'rise', defaultValue: '0.2' },
    ];

    expect(parameterFitText([bot], same)).toBeNull();
    expect(parameterFitText([bot], [...same, { name: 'buyAmount', defaultValue: '50' }])).toBe(
      'BTCUSDT 1h: buyAmount = 50 (по умолчанию)',
    );
    expect(parameterFitText([bot], [{ name: 'drop', defaultValue: '0.03' }])).toBe(
      'BTCUSDT 1h: без rise',
    );
  });
});
