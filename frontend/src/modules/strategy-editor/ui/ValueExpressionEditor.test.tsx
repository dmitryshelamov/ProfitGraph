import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { useState } from 'react';

import type { BlockCatalog, StrategyDefinition, ValueExpression } from '@/modules/strategies';
import catalogData from '@/test/blockCatalog.fixture.json';

import { ValueExpressionEditor } from './ValueExpressionEditor';

const catalog = catalogData as unknown as BlockCatalog;
const definition: StrategyDefinition = {
  parameters: [{ name: 'period', valueType: 'Integer', defaultValue: '20' }],
  values: [
    { name: 'shared', expression: { kind: 'candle', field: 'Close' } },
    { name: 'tagValue', expression: { kind: 'constant', valueType: 'Tag', text: 'entry' } },
  ],
  blocks: [],
  wires: [],
};
function Field({
  initial = { kind: 'constant', valueType: 'Price', number: '100' },
}: {
  initial?: ValueExpression;
}) {
  const [expression, setExpression] = useState<ValueExpression>(initial);
  return (
    <>
      <ValueExpressionEditor
        label="Значение"
        expression={expression}
        definition={definition}
        catalog={catalog}
        onChange={setExpression}
      />
      <output aria-label="Выражение">{JSON.stringify(expression)}</output>
    </>
  );
}
afterEach(cleanup);

it('creates an indicator expression and references a shared named period', () => {
  render(<Field />);
  fireEvent.change(screen.getByLabelText('Источник Значение'), { target: { value: 'SMA' } });
  fireEvent.change(screen.getByLabelText('Источник Значение: Период'), {
    target: { value: 'parameter' },
  });
  fireEvent.change(screen.getByLabelText('Ссылка Значение: Период'), {
    target: { value: 'period' },
  });
  expect(JSON.parse(screen.getByLabelText('Выражение').textContent!)).toEqual({
    kind: 'SMA',
    arguments: {
      in: { kind: 'candle', field: 'Close' },
      period: { kind: 'parameter', name: 'period' },
    },
  });
});

it('edits arithmetic operands recursively using named values and candle fields', () => {
  render(<Field />);
  fireEvent.change(screen.getByLabelText('Источник Значение'), { target: { value: 'arithmetic' } });
  fireEvent.change(screen.getByLabelText('Операция Значение'), { target: { value: 'Multiply' } });
  fireEvent.change(screen.getByLabelText('Источник Значение: Первое значение'), {
    target: { value: 'value' },
  });
  fireEvent.change(screen.getByLabelText('Ссылка Значение: Первое значение'), {
    target: { value: 'shared' },
  });
  fireEvent.change(screen.getByLabelText('Источник Значение: Второе значение'), {
    target: { value: 'candle' },
  });
  fireEvent.change(screen.getByLabelText('Поле Значение: Второе значение'), {
    target: { value: 'High' },
  });
  expect(JSON.parse(screen.getByLabelText('Выражение').textContent!)).toEqual({
    kind: 'arithmetic',
    operation: 'Multiply',
    arguments: {
      a: { kind: 'value', name: 'shared' },
      b: { kind: 'candle', field: 'High' },
    },
  });
});

it('uses an exact numeric OrderId constant', () => {
  render(<Field />);
  fireEvent.change(screen.getByLabelText('Тип Значение'), { target: { value: 'OrderId' } });
  expect(JSON.parse(screen.getByLabelText('Выражение').textContent!)).toEqual({
    kind: 'constant',
    valueType: 'OrderId',
    number: '1',
  });
});
it('preserves and edits an active-order tag expression rather than replacing it with text', () => {
  render(
    <Field
      initial={{
        kind: 'activeOrder',
        field: 'price',
        arguments: { tag: { kind: 'value', name: 'tagValue' } },
      }}
    />,
  );
  expect(screen.getByLabelText('Источник Значение: Метка ордера')).toHaveValue('value');
  expect(screen.getByLabelText('Ссылка Значение: Метка ордера')).toHaveValue('tagValue');
  fireEvent.change(screen.getByLabelText('Источник Значение: Метка ордера'), {
    target: { value: 'constant' },
  });
  fireEvent.change(screen.getByLabelText('Значение Значение: Метка ордера'), {
    target: { value: 'changed' },
  });
  expect(JSON.parse(screen.getByLabelText('Выражение').textContent!)).toEqual({
    kind: 'activeOrder',
    field: 'price',
    text: null,
    arguments: { tag: { kind: 'constant', valueType: 'Tag', text: 'changed' } },
  });
});

it('shows an unset required operand explicitly rather than a phantom zero', () => {
  render(
    <ValueExpressionEditor
      label="Сумма"
      expression={null}
      definition={definition}
      catalog={catalog}
      onChange={vi.fn()}
    />,
  );
  expect(screen.getByLabelText('Источник Сумма')).toHaveValue('');
  expect(screen.queryByLabelText('Значение Сумма')).not.toBeInTheDocument();
});

it('selects the Integer position holding period from the catalog', () => {
  render(<Field />);
  fireEvent.change(screen.getByLabelText('Источник Значение'), { target: { value: 'position' } });
  expect(screen.getByRole('option', { name: 'Свечей в позиции' })).toBeInTheDocument();
  fireEvent.change(screen.getByLabelText('Поле Значение'), { target: { value: 'barsHeld' } });
  expect(JSON.parse(screen.getByLabelText('Выражение').textContent!)).toEqual({
    kind: 'position',
    field: 'barsHeld',
  });
  expect(
    catalog.valueSources
      .find((source) => source.key === 'position')
      ?.fields?.find((field) => field.key === 'barsHeld')?.valueTypes,
  ).toEqual(['Integer']);
});

it('shows a custom block parameter default with its display metadata', () => {
  render(
    <ValueExpressionEditor
      label="Порог"
      expression={{ kind: 'parameter', name: 'takeProfit' }}
      definition={{
        ...definition,
        parameters: [
          {
            name: 'takeProfit',
            valueType: 'Scalar',
            defaultValue: '0.0018',
            displayUnit: 'percent',
          },
        ],
      }}
      catalog={catalog}
      onChange={vi.fn()}
    />,
  );
  expect(screen.getByText('0.18%')).toHaveAttribute('title', '0.0018');
});
