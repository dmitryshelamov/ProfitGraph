import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';

import type { BlockCatalog, StrategyDefinition } from '@/modules/strategies';
import catalogData from '@/test/blockCatalog.fixture.json';

import { upsertValue } from '../model/editorCommands';
import { useEditorStore } from '../model/editorStore';
import { StrategyValuesPanel } from './StrategyValuesPanel';

const catalog = catalogData as unknown as BlockCatalog;
const definition: StrategyDefinition = {
  parameters: [
    { name: 'allocation', valueType: 'QuoteAmount', defaultValue: '100' },
    { name: 'unused', valueType: 'Scalar', defaultValue: '10' },
  ],
  values: [
    {
      name: 'budget',
      expression: {
        kind: 'arithmetic',
        operation: 'Multiply',
        arguments: {
          a: { kind: 'parameter', name: 'allocation' },
          b: { kind: 'constant', valueType: 'Scalar', number: '0.5' },
        },
      },
    },
    { name: 'sharedBudget', expression: { kind: 'value', name: 'budget' } },
    { name: 'unusedValue', expression: { kind: 'constant', valueType: 'Price', number: '1' } },
  ],
  blocks: [
    {
      id: 'entry',
      kind: 'order',
      displayName: 'Вход после сигнала',
      orderKind: 'Market',
      side: 'Buy',
      amount: { kind: 'value', name: 'sharedBudget' },
    },
  ],
  wires: [],
};
function Panel() {
  const draft = useEditorStore((state) => state.draft)!;
  return <StrategyValuesPanel definition={draft.definition} catalog={catalog} diagnostics={[]} />;
}
beforeEach(() =>
  useEditorStore
    .getState()
    .load('strategy', 1, { name: 'Strategy', definition, layout: { positions: {} } }, true),
);
afterEach(cleanup);

it('disables used parameter/value deletion and immediately identifies their shared values and visible blocks', () => {
  render(<Panel />);
  expect(screen.getByRole('button', { name: 'Удалить параметр allocation' })).toBeDisabled();
  expect(
    screen.getByText(
      'Нельзя удалить параметр «allocation»: используется в значении «budget», значении «sharedBudget», блоке «Вход после сигнала».',
    ),
  ).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Удалить значение budget' })).toBeDisabled();
  expect(
    screen.getByText(
      'Нельзя удалить значение «budget»: используется в значении «sharedBudget», блоке «Вход после сигнала».',
    ),
  ).toBeInTheDocument();
  const before = useEditorStore.getState().draft;
  fireEvent.click(screen.getByRole('button', { name: 'Удалить параметр allocation' }));
  expect(useEditorStore.getState().draft).toBe(before);
  expect(useEditorStore.getState().past).toHaveLength(0);
});

it.each([
  ['parameter', 'Удалить параметр unused'],
  ['value', 'Удалить значение unusedValue'],
] as const)('deletes an unused %s in one undo step', (kind, label) => {
  render(<Panel />);
  expect(screen.getByRole('button', { name: label })).toBeEnabled();
  fireEvent.click(screen.getByRole('button', { name: label }));
  expect(screen.queryByRole('button', { name: label })).not.toBeInTheDocument();
  expect(useEditorStore.getState().past).toHaveLength(1);
  expect(
    useEditorStore.getState().draft!.definition[kind === 'parameter' ? 'values' : 'parameters'],
  ).toEqual(definition[kind === 'parameter' ? 'values' : 'parameters']);
  act(() => useEditorStore.getState().undo());
  expect(screen.getByRole('button', { name: label })).toBeEnabled();
});

it('enables deletion as soon as the last reference is removed', () => {
  render(<Panel />);
  expect(screen.getByRole('button', { name: 'Удалить параметр allocation' })).toBeDisabled();
  act(() =>
    useEditorStore.getState().execute(
      upsertValue({
        name: 'budget',
        expression: { kind: 'constant', valueType: 'QuoteAmount', number: '50' },
      }),
    ),
  );
  expect(screen.getByRole('button', { name: 'Удалить параметр allocation' })).toBeEnabled();
  expect(screen.queryByText(/^Нельзя удалить параметр «allocation»/)).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Удалить значение budget' })).toBeDisabled();
});

it('renders dependency cycles safely and prevents deleting their externally referenced members', () => {
  useEditorStore.getState().load(
    'strategy',
    1,
    {
      name: 'Cycle',
      layout: { positions: {} },
      definition: {
        parameters: [],
        blocks: [],
        wires: [],
        values: [
          { name: 'first', expression: { kind: 'value', name: 'second' } },
          { name: 'second', expression: { kind: 'value', name: 'first' } },
        ],
      },
    },
    true,
  );
  render(<Panel />);
  expect(screen.getByRole('button', { name: 'Удалить значение first' })).toBeDisabled();
  expect(screen.getByRole('button', { name: 'Удалить значение second' })).toBeDisabled();
  expect(
    screen.getByText('Нельзя удалить значение «first»: используется в значении «second».'),
  ).toBeInTheDocument();
});

it('renames a used parameter and named value with all references in one undo step', () => {
  render(<Panel />);
  fireEvent.change(screen.getByLabelText('Новое имя allocation'), {
    target: { value: 'investment' },
  });
  fireEvent.submit(screen.getByLabelText('Новое имя allocation').closest('form')!);
  expect(useEditorStore.getState().draft!.definition.values[0]!.expression.arguments!.a!.name).toBe(
    'investment',
  );
  expect(screen.getByLabelText('Значение параметра investment')).toHaveValue('100');
  act(() => useEditorStore.getState().undo());
  expect(screen.getByLabelText('Значение параметра allocation')).toHaveValue('100');
  fireEvent.change(screen.getByLabelText('Новое имя budget'), { target: { value: 'capital' } });
  fireEvent.submit(screen.getByLabelText('Новое имя budget').closest('form')!);
  expect(useEditorStore.getState().draft!.definition.values[1]!.expression.name).toBe('capital');
  act(() => useEditorStore.getState().undo());
  expect(useEditorStore.getState().draft!.definition).toEqual(definition);
});
it('excludes values depending on the edited value from the reference picker and keeps a valid source-switch default', () => {
  render(<Panel />);
  fireEvent.change(screen.getByLabelText('Источник budget'), { target: { value: 'value' } });
  const picker = screen.getByLabelText('Ссылка budget');
  expect(picker).not.toContainHTML('value="sharedBudget"');
  expect(picker).not.toContainHTML('value="budget"');
  expect(useEditorStore.getState().draft!.definition.values[0]!.expression.name).toBe(
    'unusedValue',
  );
});

it('edits Ladder percentages with decimal drafts and stores exact fractions through undo', () => {
  act(() =>
    useEditorStore.getState().load(
      'strategy',
      1,
      {
        name: 'Ladder',
        definition: {
          ...definition,
          templateKind: 'Ladder',
          parameters: [
            { name: 'drop', valueType: 'Scalar', defaultValue: '0.6', displayUnit: 'percent' },
          ],
        },
        layout: { positions: {} },
      },
      true,
    ),
  );
  render(<Panel />);
  const input = screen.getByRole('textbox', { name: 'Значение параметра drop, %' });
  expect(input).toHaveValue('60');
  fireEvent.change(input, { target: { value: '0.' } });
  expect(input).toHaveValue('0.');
  fireEvent.change(input, { target: { value: '0.6' } });
  expect(input).toHaveValue('0.6');
  expect(useEditorStore.getState().draft!.definition.parameters[0]!.defaultValue).toBe('0.006');
  fireEvent.blur(input);
  act(() => useEditorStore.getState().undo());
  expect(input).toHaveValue('60');
});

it.each([
  ['01', '0.01'],
  ['.5', '0.005'],
  ['1.', '0.01'],
])('converts the human decimal draft %s to fraction %s', (inputValue, stored) => {
  act(() =>
    useEditorStore.getState().load(
      'strategy',
      1,
      {
        name: 'Ladder',
        definition: {
          ...definition,
          templateKind: 'Ladder',
          parameters: [
            { name: 'rise', valueType: 'Scalar', defaultValue: '0.1', displayUnit: 'percent' },
          ],
        },
        layout: { positions: {} },
      },
      true,
    ),
  );
  render(<Panel />);
  const input = screen.getByRole('textbox', { name: 'Значение параметра rise, %' });
  fireEvent.change(input, { target: { value: inputValue } });
  expect(useEditorStore.getState().draft!.definition.parameters[0]!.defaultValue).toBe(stored);
  fireEvent.blur(input);
  act(() => useEditorStore.getState().undo());
  expect(input).toHaveValue('10');
});

it('keeps invalid percentage drafts invalid in the definition rather than interpreting them as fractions', () => {
  act(() =>
    useEditorStore.getState().load(
      'strategy',
      1,
      {
        name: 'Ladder',
        definition: {
          ...definition,
          templateKind: 'Ladder',
          parameters: [
            { name: 'rise', valueType: 'Scalar', defaultValue: '0.1', displayUnit: 'percent' },
          ],
        },
        layout: { positions: {} },
      },
      true,
    ),
  );
  render(<Panel />);
  const input = screen.getByRole('textbox', { name: 'Значение параметра rise, %' });
  fireEvent.change(input, { target: { value: '1e-2' } });
  expect(useEditorStore.getState().draft!.definition.parameters[0]!.defaultValue).toBe('1e-2%');
  expect(screen.getByText('Введите точное число с точкой.')).toBeInTheDocument();
  fireEvent.blur(input);
  expect(input).toHaveValue('1e-2');
});

it('selects percentage metadata and keeps edited defaults as fractions for saving', () => {
  render(<Panel />);
  fireEvent.change(screen.getByLabelText('Единица параметра unused'), {
    target: { value: 'percent' },
  });
  expect(screen.getByRole('textbox', { name: 'Значение параметра unused, %' })).toHaveValue('1000');
  fireEvent.change(screen.getByRole('textbox', { name: 'Значение параметра unused, %' }), {
    target: { value: '0.18' },
  });
  expect(
    useEditorStore
      .getState()
      .draft!.definition.parameters.find((parameter) => parameter.name === 'unused'),
  ).toMatchObject({ displayUnit: 'percent', defaultValue: '0.0018' });
  fireEvent.change(screen.getByLabelText('Единица параметра unused'), {
    target: { value: 'none' },
  });
  expect(screen.getByRole('textbox', { name: 'Значение параметра unused' })).toHaveValue('0.0018');
});
