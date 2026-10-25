import { backtestLimitTexts } from './backtestLimits';

describe('backtest limits', () => {
  it('turns known codes into fixed texts and skips unknown ones', () => {
    expect(backtestLimitTexts(['price_band', 'something_new', 'no_trades_fill_nothing'])).toEqual([
      'Ценовой коридор биржи не моделируется.',
      'На свече без сделок ордера не исполняются.',
    ]);
    expect(backtestLimitTexts(undefined)).toEqual([]);
  });
});
