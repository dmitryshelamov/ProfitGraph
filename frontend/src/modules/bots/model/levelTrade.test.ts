import { levelTrade, levelTradeLevels, percentFrom, plannedOutcome, stopRule } from './levelTrade';

const long = { entryPrice: '100', stopPrice: '95', targetPrice: '110', buyAmount: '50' };
const short = { entryPrice: '100', stopPrice: '104', targetPrice: '92', sellAmount: '40' };

describe('level trade', () => {
  it('reads a long trade from the level trade strategy’s parameters', () => {
    expect(levelTrade({ parameters: long })).toEqual({
      direction: 'Long',
      amount: '50',
      entry: '100',
      stop: '95',
      target: '110',
    });
  });

  it('reads a short trade from the short mirror’s sell amount', () => {
    expect(levelTrade({ parameters: short })).toMatchObject({ direction: 'Short', amount: '40' });
  });

  it('is absent for another strategy or a broken value', () => {
    expect(levelTrade({ parameters: { buyAmount: '25', drop: '0.015' } })).toBeNull();
    expect(levelTrade({ parameters: { ...long, stopPrice: 'x' } })).toBeNull();
    expect(levelTrade({ parameters: { ...long, targetPrice: '0' } })).toBeNull();
    expect(
      levelTrade({ parameters: { entryPrice: '1', stopPrice: '1', targetPrice: '1' } }),
    ).toBeNull();
  });

  it('measures a price from a reference in signed percent rounded down to hundredths', () => {
    expect(percentFrom('95', '105')).toBe('−9.52%');
    expect(percentFrom('110', '105')).toBe('+4.76%');
    expect(percentFrom('105', '105')).toBe('0%');
    expect(percentFrom('105', '0')).toBeNull();
  });

  it('plans the loss at the stop and the profit at the target before fees', () => {
    expect(plannedOutcome(levelTrade({ parameters: long })!)).toEqual({
      loss: '2.5',
      profit: '5',
    });
    expect(plannedOutcome(levelTrade({ parameters: short })!)).toEqual({
      loss: '1.6',
      profit: '3.2',
    });
  });

  it('says the stop is a market exit on a closed candle of the bot’s timeframe', () => {
    expect(stopRule(levelTrade({ parameters: long })!, '1h')).toBe(
      'Продажа по рынку, когда свеча 1h закрылась на уровне стопа или ниже',
    );
    expect(stopRule(levelTrade({ parameters: short })!, '15m')).toBe(
      'Покупка по рынку, когда свеча 15m закрылась на уровне стопа или выше',
    );
  });

  it('draws the entry, target and stop as solid lines and folds a working order at the same price into its line', () => {
    const target = { id: '7', side: 'Sell', price: '110.0', title: 'Продажа 110 × 0.5' } as const;
    const other = { id: '8', side: 'Buy', price: '90', title: 'Покупка 90 × 1' } as const;

    expect(levelTradeLevels(levelTrade({ parameters: long })!, '1h', [target, other], 2)).toEqual([
      {
        id: 'level-trade:entry',
        side: 'Buy',
        price: '100',
        title: 'Вход 100',
        solid: true,
        neutral: true,
      },
      {
        id: 'level-trade:target',
        side: 'Buy',
        price: '110',
        title: 'Цель 110 · Продажа 110 × 0.5',
        solid: true,
      },
      {
        id: 'level-trade:stop',
        side: 'Sell',
        price: '95',
        title: 'Стоп 95 · по закрытию 1h',
        solid: true,
      },
      other,
    ]);
  });
});
