import catalog from '@/test/blockCatalog.fixture.json';

import { allowedValueLabel, fieldLabel, valueTypeLabel } from './catalogLabels';

it('has Russian labels and typed operands in the block catalog fixture', () => {
  for (const entry of [
    ...catalog.blockKinds,
    ...catalog.eventSources,
    ...catalog.checkTypes,
    ...catalog.orderKinds,
    ...catalog.valueSources,
  ]) {
    expect(entry.label).not.toBe(entry.key);
    expect(entry.label).toMatch(/[А-Яа-я]/);
    for (const operand of entry.operands) {
      expect(operand.valueTypes.length).toBeGreaterThan(0);
    }
  }
});

it('translates known fields and leaves unknown catalog labels readable', () => {
  expect(fieldLabel('amount')).toBe('Сумма в котируемой валюте');
  expect(fieldLabel('newField', 'Human label')).toBe('Human label');
  expect(fieldLabel('newField')).toBe('newField');
  expect(valueTypeLabel('RejectionReason')).toBe('Причина отказа');
  expect(allowedValueLabel('Market')).toBe('Рыночный');
  expect(allowedValueLabel('>')).toBe('>');
});

it('uses ордер for order nodes, fields and values', () => {
  expect(fieldLabel('orderId')).toBe('ID ордера');
  expect(valueTypeLabel('Order')).toBe('Ордер');
  expect(allowedValueLabel('Order.type')).toBe('Ордер: Тип ордера');
});
