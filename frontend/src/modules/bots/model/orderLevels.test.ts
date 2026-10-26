import { orderLevels } from './orderLevels';
import type { BotOpenOrder } from './types';

const order = (overrides: Partial<BotOpenOrder>): BotOpenOrder => ({
  engineOrderId: 1,
  tag: null,
  side: 'Buy',
  type: 'Limit',
  status: 'Active',
  quantity: '10',
  limitPrice: '104.289',
  quoteBudget: null,
  filledBase: '0',
  ...overrides,
});

describe('order levels', () => {
  it('titles a limit order with side, price at the exchange precision and quantity', () => {
    expect(orderLevels([order({})], 2)).toEqual([
      { id: '1', side: 'Buy', price: '104.289', title: 'Покупка 104.29 × 10' },
    ]);
  });

  it('leaves out an order without a limit price', () => {
    const levels = orderLevels(
      [order({ engineOrderId: 2, type: 'Market', limitPrice: null, side: 'Sell' })],
      null,
    );

    expect(levels).toEqual([]);
  });
});
