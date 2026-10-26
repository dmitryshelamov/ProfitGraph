import { checkManualOrder, rejectionLabel } from './manualOrder';

// A T-Invest share: GAZP trades in lots of 10 shares on a 0.01 tick.
const gazp = {
  quantityStep: '10',
  priceTick: '0.01',
  minQuantity: '10',
  minNotional: '0',
  basePrecision: 0,
};

describe('checkManualOrder', () => {
  it('lets a market order through without rules', () => {
    expect(
      checkManualOrder({ type: 'Market', side: 'Buy', amount: '100', price: '' }, undefined),
    ).toEqual({
      error: null,
      quantity: null,
    });
  });

  it('waits for an amount and refuses a bad one', () => {
    expect(
      checkManualOrder({ type: 'Limit', side: 'Buy', amount: '', price: '' }, gazp).error,
    ).toBeNull();
    expect(
      checkManualOrder({ type: 'Limit', side: 'Buy', amount: '0', price: '1' }, gazp).error,
    ).toBe('Сумма — положительное число.');
    expect(
      checkManualOrder({ type: 'Limit', side: 'Buy', amount: '1e3', price: '1' }, gazp).error,
    ).toBe('Сумма — положительное число.');
  });

  it('refuses a price off the tick', () => {
    expect(
      checkManualOrder({ type: 'Limit', side: 'Sell', amount: '10', price: '150.255' }, gazp).error,
    ).toBe('Цена должна быть кратна шагу цены 0.01.');
    expect(
      checkManualOrder({ type: 'Limit', side: 'Sell', amount: '10', price: '-1' }, gazp).error,
    ).toBe('Цена — положительное число.');
  });

  it('sizes a limit buy in whole lots, rounding down', () => {
    expect(
      checkManualOrder({ type: 'Limit', side: 'Buy', amount: '3000', price: '150' }, gazp),
    ).toEqual({
      error: null,
      quantity: '20',
    });
    expect(
      checkManualOrder({ type: 'Limit', side: 'Buy', amount: '3299', price: '150' }, gazp).quantity,
    ).toBe('20');
  });

  it('refuses an amount that rounds to no lot', () => {
    const check = checkManualOrder(
      { type: 'Limit', side: 'Buy', amount: '1000', price: '150' },
      gazp,
    );

    expect(check.quantity).toBe('0');
    expect(check.error).toContain('округляется вниз до шага');
  });

  it('rounds a limit sell down to the lot', () => {
    expect(
      checkManualOrder({ type: 'Limit', side: 'Sell', amount: '37', price: '150' }, gazp),
    ).toEqual({
      error: null,
      quantity: '30',
    });
  });

  it('checks the minimum quantity and notional', () => {
    const rules = {
      ...gazp,
      quantityStep: '0.001',
      minQuantity: '0.01',
      minNotional: '5',
      basePrecision: 6,
    };

    expect(
      checkManualOrder({ type: 'Limit', side: 'Sell', amount: '0.005', price: '100' }, rules).error,
    ).toBe('Меньше минимального количества 0.01.');
    expect(
      checkManualOrder({ type: 'Limit', side: 'Sell', amount: '0.02', price: '100' }, rules).error,
    ).toBe('Меньше минимальной суммы заявки 5.');
    expect(
      checkManualOrder({ type: 'Limit', side: 'Sell', amount: '0.1', price: '100' }, rules).error,
    ).toBeNull();
  });

  it('checks only what is typed while the rules load', () => {
    expect(
      checkManualOrder({ type: 'Limit', side: 'Buy', amount: '10', price: '1.234' }, undefined)
        .error,
    ).toBeNull();
  });
});

describe('rejectionLabel', () => {
  it('names the known codes and shows an unknown one as it is', () => {
    expect(rejectionLabel('TagActive')).toBe('уже стоит другая ручная заявка');
    expect(rejectionLabel('SomethingNew')).toBe('SomethingNew');
  });
});
