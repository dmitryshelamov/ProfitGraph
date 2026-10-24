import type { StrategyDefinition } from '@/modules/strategies';

import { renameParameter, renameValue, upsertValue } from './editorCommands';
import { useEditorStore } from './editorStore';
import { wouldCreateValueCycle } from './namedSettingEdits';

const definition: StrategyDefinition = {
  parameters: [
    { name: 'budget', valueType: 'QuoteAmount', defaultValue: '9007199254740993.123456789' },
  ],
  values: [
    { name: 'a', expression: { kind: 'parameter', name: 'budget' } },
    {
      name: 'b',
      expression: { kind: 'arithmetic', arguments: { left: { kind: 'value', name: 'a' } } },
    },
  ],
  blocks: [
    {
      id: 'buy',
      kind: 'order',
      side: 'Buy',
      amount: { kind: 'value', name: 'b' },
      quantity: { kind: 'parameter', name: 'budget' },
      tag: { kind: 'activeOrder', arguments: { tag: { kind: 'parameter', name: 'budget' } } },
      checks: [
        {
          type: 'Compare',
          tag: { kind: 'value', name: 'a' },
          operands: { a: { kind: 'parameter', name: 'budget' } },
        },
      ],
    },
  ],
  wires: [],
};
beforeEach(() =>
  useEditorStore
    .getState()
    .load('s', 1, { name: 'Strategy', definition, layout: { positions: {} } }, true),
);

it('renames a parameter atomically across nested values, inactive block fields, check operands and tags with one undo', () => {
  const store = useEditorStore.getState();
  store.execute(renameParameter('budget', 'allocation'));
  const next = useEditorStore.getState().draft!.definition;
  expect(next.parameters[0]).toEqual({ ...definition.parameters[0], name: 'allocation' });
  expect(next.values[0]!.expression.name).toBe('allocation');
  expect(next.blocks[0]!.quantity!.name).toBe('allocation');
  expect(next.blocks[0]!.tag!.arguments!.tag!.name).toBe('allocation');
  expect(next.blocks[0]!.checks![0]!.operands!.a!.name).toBe('allocation');
  expect(useEditorStore.getState().past).toHaveLength(1);
  store.undo();
  expect(useEditorStore.getState().draft!.definition).toEqual(definition);
  expect(useEditorStore.getState().dirty).toBe(false);
});
it('renames a named value without touching exact decimals or tag literals and restores references on undo', () => {
  useEditorStore.getState().execute(renameValue('a', 'renamed'));
  const next = useEditorStore.getState().draft!.definition;
  expect(next.values[0]!.name).toBe('renamed');
  expect(next.values[1]!.expression.arguments!.left!.name).toBe('renamed');
  expect(next.blocks[0]!.checks![0]!.tag!.name).toBe('renamed');
  expect(next.parameters[0]!.defaultValue).toBe(definition.parameters[0]!.defaultValue);
  useEditorStore.getState().undo();
  expect(useEditorStore.getState().draft!.definition).toEqual(definition);
});
it.each(['', 'budget', 'b'])(
  'rejects invalid or colliding names %s without history changes',
  (name) => {
    expect(() => useEditorStore.getState().execute(renameValue('a', name))).toThrow(
      'уникальное имя',
    );
    expect(useEditorStore.getState().draft!.definition).toEqual(definition);
    expect(useEditorStore.getState().past).toHaveLength(0);
  },
);
it('rejects direct and transitive cycles through nested expression arguments atomically', () => {
  expect(wouldCreateValueCycle(definition, 'a', { kind: 'value', name: 'b' })).toBe(true);
  expect(() =>
    useEditorStore.getState().execute(
      upsertValue({
        name: 'a',
        expression: { kind: 'activeOrder', arguments: { tag: { kind: 'value', name: 'b' } } },
      }),
    ),
  ).toThrow('цикл ссылок');
  expect(() =>
    useEditorStore
      .getState()
      .execute(upsertValue({ name: 'a', expression: { kind: 'value', name: 'a' } })),
  ).toThrow('цикл ссылок');
  expect(useEditorStore.getState().past).toHaveLength(0);
  expect(useEditorStore.getState().dirty).toBe(false);
});
it('loads an existing cyclic draft and allows repairing the cycle without blocking unrelated edits', () => {
  const cyclic = {
    ...definition,
    values: [
      { name: 'a', expression: { kind: 'value', name: 'b' } },
      { name: 'b', expression: { kind: 'value', name: 'a' } },
    ],
  };
  useEditorStore
    .getState()
    .load('s', 1, { name: 'Invalid', definition: cyclic, layout: { positions: {} } }, true);
  expect(wouldCreateValueCycle(cyclic, 'unrelated', { kind: 'value', name: 'a' })).toBe(false);
  useEditorStore
    .getState()
    .execute(
      upsertValue({ name: 'a', expression: { kind: 'constant', valueType: 'Price', number: '1' } }),
    );
  expect(
    wouldCreateValueCycle(useEditorStore.getState().draft!.definition, 'a', {
      kind: 'value',
      name: 'b',
    }),
  ).toBe(true);
  expect(useEditorStore.getState().past).toHaveLength(1);
});
