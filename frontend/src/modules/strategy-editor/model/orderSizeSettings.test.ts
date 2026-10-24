import type { StrategyBlock } from '@/modules/strategies';
import type { Schemas } from '@/shared/api/client';

import {
  needsOrderConversion,
  orderSizeMode,
  previewOrder,
  showsOrderPrice,
  withMinimumOrderPolicy,
  withOrderKind,
  withOrderSide,
  withOrderSizeMode,
  withPreviewSettings,
} from './orderSizeSettings';

const order: StrategyBlock = {
  id: 'order',
  kind: 'order',
  orderKind: 'Limit',
  side: 'Buy',
  sizeMode: 'amount',
  amount: { kind: 'constant', valueType: 'QuoteAmount', number: '9007199254740993.123456789' },
  quantity: { kind: 'constant', valueType: 'BaseAmount', number: '0.0000000000000000000000000001' },
  price: { kind: 'constant', valueType: 'Price', number: '123.123456789012345678' },
};

it('keeps rejection implicit and removes the optional raise policy when reset or changed to a sale', () => {
  expect(withMinimumOrderPolicy(order, 'Reject')).toEqual(order);
  expect(JSON.stringify(withMinimumOrderPolicy(order, 'Reject'))).toBe(JSON.stringify(order));
  const raised = withMinimumOrderPolicy(order, 'RaiseToExchangeMinimum');
  expect(raised.minimumOrderPolicy).toBe('RaiseToExchangeMinimum');
  expect(withOrderKind(raised, 'Market').minimumOrderPolicy).toBe('RaiseToExchangeMinimum');
  expect(withMinimumOrderPolicy(raised, 'Reject')).not.toHaveProperty('minimumOrderPolicy');
  expect(withOrderSide(raised, 'Sell')).not.toHaveProperty('minimumOrderPolicy');
});

it('preserves the preview raise and exact quote limit, and omits the default policy', () => {
  const check = {
    type: 'OrderSizePreviewValid',
    side: 'Buy',
    orderKind: 'Limit',
    operands: { amount: order.amount!, quoteLimit: order.amount! },
  };
  const raised = withPreviewSettings(check, { minimumOrderPolicy: 'RaiseToExchangeMinimum' });
  expect(previewOrder(raised).minimumOrderPolicy).toBe('RaiseToExchangeMinimum');
  expect(raised.operands!.quoteLimit).toBe(check.operands.quoteLimit);
  expect(withPreviewSettings(raised, { minimumOrderPolicy: 'Reject' })).not.toHaveProperty(
    'minimumOrderPolicy',
  );
  expect(withPreviewSettings(raised, { side: 'Sell' })).not.toHaveProperty('minimumOrderPolicy');
});

it.each([
  ['Buy', 'amount'],
  ['Sell', 'quantity'],
] as const)('infers the legacy %s size mode as %s', (side, expected) => {
  expect(orderSizeMode({ ...order, side, sizeMode: null })).toBe(expected);
  expect(orderSizeMode({ ...order, side, sizeMode: undefined })).toBe(expected);
});
it.each([
  ['Buy', 'amount', false],
  ['Buy', 'quantity', true],
  ['Sell', 'amount', true],
  ['Sell', 'quantity', false],
] as const)(
  'uses an explicit market conversion price only for %s with %s',
  (side, sizeMode, conversion) => {
    const block = { ...order, side, sizeMode, orderKind: 'Market' };
    expect(needsOrderConversion(block)).toBe(conversion);
    expect(showsOrderPrice(block)).toBe(conversion);
    expect(showsOrderPrice({ ...block, orderKind: 'Limit' })).toBe(true);
  },
);

it('preserves both exact size expressions and the entered price across side, mode, kind and API reload', () => {
  let next = withOrderSide(order, 'Sell');
  expect(orderSizeMode(next)).toBe('amount');
  next = withOrderSizeMode(next, 'quantity');
  next = withOrderKind(next, 'Market');
  expect(showsOrderPrice(next)).toBe(false);
  next = withOrderSide(next, 'Buy');
  expect(orderSizeMode(next)).toBe('quantity');
  expect(showsOrderPrice(next)).toBe(true);
  expect(next.amount).toBe(order.amount);
  expect(next.quantity).toBe(order.quantity);
  expect(next.price).toBe(order.price);
  const request: Schemas['CreateStrategyRequest'] = {
    name: 'Sized order',
    definition: { parameters: [], values: [], blocks: [next], wires: [] },
    layout: { positions: {} },
  };
  const reloaded = JSON.parse(JSON.stringify(request)) as Schemas['CreateStrategyRequest'];
  expect(reloaded.definition.blocks[0]).toEqual(next);
  expect(reloaded.definition.blocks[0]!.amount!.number).toBe('9007199254740993.123456789');
  expect(reloaded.definition.blocks[0]!.quantity!.number).toBe('0.0000000000000000000000000001');
  expect(orderSizeMode(reloaded.definition.blocks[0]!)).toBe('quantity');
});

it('freezes an inferred size mode when side changes instead of erasing the old input', () => {
  const legacy = { ...order, sizeMode: null, price: null, orderKind: 'Market' };
  const next = withOrderSide(legacy, 'Sell');
  expect(next.sizeMode).toBe('amount');
  expect(next.amount).toBe(legacy.amount);
  expect(next.quantity).toBe(legacy.quantity);
  expect(next.price).toEqual({ kind: 'candle', field: 'Close' });
});
it('leaves the newly selected size source explicitly unset and initializes a conversion price when needed', () => {
  const native = {
    id: 'order',
    kind: 'order',
    orderKind: 'Market',
    side: 'Buy',
    amount: order.amount,
  };
  const next = withOrderSizeMode(native, 'quantity');
  expect(next.quantity).toBeUndefined();
  expect(next.amount).toBe(native.amount);
  expect(next.price).toEqual({ kind: 'candle', field: 'Close' });
});

it('switches whole-base sale to an explicitly selected buy budget while retaining all expressions and custom names', () => {
  const sale = {
    ...order,
    side: 'Sell',
    sizeMode: 'quantity',
    displayName: 'Продажа',
    quantity: { kind: 'balance', field: 'baseAvailable' },
  };
  const buy = withOrderSide(sale, 'Buy');
  expect(buy.sizeMode).toBe('amount');
  expect(buy.amount).toBe(sale.amount);
  expect(buy.quantity).toBe(sale.quantity);
  expect(buy.displayName).toBe('Покупка');
  expect(withOrderSide({ ...sale, displayName: 'Выход после сигнала' }, 'Buy').displayName).toBe(
    'Выход после сигнала',
  );
});

it('initializes preview operands for the selected mode and retains typed values across kind toggles', () => {
  const first = withPreviewSettings(
    {
      type: 'OrderSizePreviewValid',
      side: 'Buy',
      orderKind: 'Market',
      operands: { amount: order.amount! },
    },
    { side: 'Sell' },
  );
  expect(first.sizeMode).toBe('quantity');
  expect(first.operands!.quantity).toEqual({
    kind: 'constant',
    valueType: 'BaseAmount',
    number: '0',
  });
  const inverse = withPreviewSettings(first, { sizeMode: 'amount' });
  expect(inverse.operands!.amount).toBe(order.amount);
  expect(inverse.operands!.price).toEqual({ kind: 'candle', field: 'Close' });
  expect(withPreviewSettings(inverse, { orderKind: 'Limit' }).operands).toEqual(inverse.operands);
  expect(
    withPreviewSettings(inverse, { sizeMode: 'quantity', orderKind: 'Market' }).operands,
  ).toEqual(inverse.operands);
});
