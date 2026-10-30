import { ideaOutcome } from './ideaOutcome';
import type { ScanTradeIdea } from './types';

const long: ScanTradeIdea = {
  timeframe: '15m',
  scenario: 'Target2R',
  entry: '100',
  stop: '95',
  target: '110',
  rewardRisk: '2',
  entryValidityMinutes: 60,
  amount: '100',
  risk: '5.2',
  taken: false,
  direction: 'Long',
};

describe('idea outcome', () => {
  it('takes the fees the sizing assumed off the profit at the target', () => {
    expect(ideaOutcome(long)).toEqual({
      profit: '9.8',
      profitPercent: '9.8',
      loss: '5.2',
      lossPercent: '5.2',
      netRewardRisk: '1.88461538',
      targetMove: '10',
      stopMove: '-5',
    });
  });

  it('measures a short from its entry down to the target', () => {
    const outcome = ideaOutcome({ ...long, direction: 'Short', stop: '105', target: '90' });

    expect(outcome.profit).toBe('9.8');
    expect(outcome.targetMove).toBe('-10');
    expect(outcome.stopMove).toBe('5');
  });

  it('shows a target that does not pay its fees as a loss', () => {
    const outcome = ideaOutcome({ ...long, target: '100.1' });

    expect(outcome.profit).toBe('-0.1');
    expect(outcome.netRewardRisk).toBe('-0.01923076');
  });

  it('keeps the price moves of an idea that is not sized yet', () => {
    expect(ideaOutcome({ ...long, amount: null, risk: null })).toEqual({
      profit: null,
      profitPercent: null,
      loss: null,
      lossPercent: null,
      netRewardRisk: null,
      targetMove: '10',
      stopMove: '-5',
    });
  });
});
