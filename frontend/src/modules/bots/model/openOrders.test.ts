import { openOrderPrice, openOrderQuantity, openOrderStatusLabel } from './openOrders';

describe('open orders', () => {
  it('shows a limit order by its quantity and price', () => {
    expect(openOrderQuantity({ quantity: '832.75', quoteBudget: null })).toBe('832.75');
    expect(openOrderPrice({ type: 'Limit', limitPrice: '0.03872' })).toBe('0.03872');
  });

  it('shows a market buy by its budget at the market price', () => {
    expect(openOrderQuantity({ quantity: null, quoteBudget: '30' })).toBe('на 30');
    expect(openOrderPrice({ type: 'Market', limitPrice: null })).toBe('по рынку');
  });

  it('names the states an open order can be in', () => {
    expect(openOrderStatusLabel('Active')).toBe('Стоит на бирже');
    expect(openOrderStatusLabel('PendingSubmit')).toBe('Отправляется');
    expect(openOrderStatusLabel('PendingCancel')).toBe('Отменяется');
  });
});
