import type { StrategyBlock, StrategyDefinition, ValueExpression } from '@/modules/strategies';

import {
  addBlock,
  addCheck,
  connectBlocks,
  deleteBlock,
  disconnectWire,
  insertBlock,
  keepFlowPlaces,
  moveBlocks,
  reconnectWire,
  removeCheck,
  removeParameter,
  removeValue,
  reorderCheck,
  resetFlowLayout,
  setConditionJoin,
  updateBlock,
  updateCheck,
  upsertParameter,
  upsertValue,
  wireId,
} from './editorCommands';
import type { Draft, EditorCommand } from './editorStore';
import { useEditorStore } from './editorStore';

const start: StrategyBlock = { id: 'start', kind: 'event', source: 'OnStart' };
const condition: StrategyBlock = {
  id: 'if',
  kind: 'condition',
  join: 'all',
  checks: [{ type: 'PositionFlat' }, { type: 'NoActiveOrders' }],
};
const order: StrategyBlock = { id: 'buy', kind: 'order', side: 'Buy', orderKind: 'Market' };
const initial: Draft = {
  name: 'SMA',
  definition: {
    parameters: [],
    values: [],
    blocks: [start, condition, order],
    wires: [{ from: { block: 'start', exit: 'out' }, to: 'if' }],
  },
  layout: { positions: { start: { x: 1, y: 2 }, if: { x: 3, y: 4 } } },
};
const id = wireId(initial.definition.wires[0]!);
const commands: [string, () => EditorCommand][] = [
  ['add block', () => addBlock({ id: 'cancel', kind: 'cancel' })],
  ['delete block', () => deleteBlock('if')],
  ['update block', () => updateBlock({ ...condition, displayName: 'Имя' })],
  ['connect', () => connectBlocks({ nodeId: 'if', port: 'yes' }, 'buy')],
  ['disconnect', () => disconnectWire(id)],
  ['reconnect', () => reconnectWire(id, { nodeId: 'start', port: 'out' }, 'buy')],
  ['insert', () => insertBlock({ nodeId: 'if', port: 'yes' }, { id: 'cancel', kind: 'cancel' })],
  ['add check', () => addCheck('if', { type: 'HasBaseBalance' })],
  ['update check', () => updateCheck('if', 0, { type: 'HasBaseBalance' })],
  ['remove check', () => removeCheck('if', 0)],
  ['reorder checks', () => reorderCheck('if', 0, 1)],
  ['switch join', () => setConditionJoin('if', 'any')],
  [
    'parameter',
    () =>
      upsertParameter({
        name: 'amount',
        valueType: 'QuoteAmount',
        defaultValue: '123.123456789012345678',
      }),
  ],
  ['value', () => upsertValue({ name: 'close', expression: { kind: 'candle', field: 'Close' } })],
  ['move', () => moveBlocks({ start: { x: 99, y: 100 } })],
  ['arrange', () => resetFlowLayout()],
];
describe('direct block commands', () => {
  beforeEach(() => useEditorStore.getState().load('strategy', 1, structuredClone(initial), true));
  it.each(commands)('%s is one reversible undo step', (_, make) => {
    const before = structuredClone(useEditorStore.getState().draft);
    useEditorStore.getState().execute(make());
    const after = structuredClone(useEditorStore.getState().draft);
    expect(after).not.toEqual(before);
    expect(useEditorStore.getState().past).toHaveLength(1);
    useEditorStore.getState().undo();
    expect(useEditorStore.getState().draft).toEqual(before);
    useEditorStore.getState().redo();
    expect(useEditorStore.getState().draft).toEqual(after);
    expect(initial.definition.blocks).toEqual([start, condition, order]);
  });
  it('deletes only block wires and layout', () => {
    const next = deleteBlock('if').apply(initial);
    expect(next.definition.wires).toEqual([]);
    expect(next.layout.positions.if).toBeUndefined();
    expect(initial.definition.wires).toHaveLength(1);
  });
  it('stores multiple incoming wires natively', () => {
    const first = connectBlocks({ nodeId: 'if', port: 'yes' }, 'buy').apply(initial);
    const next = connectBlocks({ nodeId: 'start', port: 'out' }, 'buy').apply(first);
    expect(next.definition.blocks).toHaveLength(3);
    expect(next.definition.wires.filter((wire) => wire.to === 'buy')).toHaveLength(2);
  });
  it('deduplicates and rejects ordinary cycles, accepting lifecycle cycles', () => {
    expect(connectBlocks({ nodeId: 'start', port: 'out' }, 'if').apply(initial)).toEqual(initial);
    const next = connectBlocks({ nodeId: 'if', port: 'yes' }, 'buy').apply(initial);
    expect(() => connectBlocks({ nodeId: 'buy', port: 'submitted' }, 'if').apply(next)).toThrow(
      'цикл',
    );
    expect(
      connectBlocks({ nodeId: 'buy', port: 'filled' }, 'if').apply(next).definition.wires,
    ).toHaveLength(3);
  });
  it('reattachment stays atomic on invalid target', () => {
    expect(() =>
      useEditorStore
        .getState()
        .execute(reconnectWire(id, { nodeId: 'start', port: 'out' }, 'missing')),
    ).toThrow();
    expect(useEditorStore.getState().draft).toEqual(initial);
    expect(useEditorStore.getState().past).toHaveLength(0);
  });
  it('upserts/removes named settings while retaining exact decimals', () => {
    let next = upsertParameter({
      name: 'amount',
      valueType: 'QuoteAmount',
      defaultValue: '1',
    }).apply(initial);
    next = upsertParameter({
      name: 'amount',
      valueType: 'QuoteAmount',
      defaultValue: '2.000000000000000001',
    }).apply(next);
    expect(next.definition.parameters).toHaveLength(1);
    expect(next.definition.parameters[0]!.defaultValue).toBe('2.000000000000000001');
    next = removeParameter('amount').apply(next);
    expect(next.definition.parameters).toEqual([]);
    next = upsertValue({ name: 'close', expression: { kind: 'candle', field: 'Close' } }).apply(
      next,
    );
    next = upsertValue({ name: 'close', expression: { kind: 'candle', field: 'Open' } }).apply(
      next,
    );
    expect(next.definition.values).toHaveLength(1);
    expect(next.definition.values[0]!.expression.field).toBe('Open');
    next = removeValue('close').apply(next);
    expect(next.definition.values).toEqual([]);
  });
  it('pins shown layout and drops deleted positions', () => {
    const next = keepFlowPlaces(deleteBlock('if'), {
      start: { x: 30, y: 40 },
      if: { x: 1, y: 2 },
    }).apply(initial);
    expect(next.layout.positions.if).toBeUndefined();
    expect(next.layout.positions.start).toEqual({ x: 1, y: 2 });
  });
});

describe('referenced named settings cannot be deleted', () => {
  const reference = (kind: 'parameter' | 'value'): ValueExpression => ({ kind, name: 'shared' });
  const nested = (kind: 'parameter' | 'value'): ValueExpression => ({
    kind: 'arithmetic',
    operation: 'Multiply',
    arguments: {
      a: { kind: 'previous', arguments: { in: reference(kind) } },
      b: { kind: 'constant', valueType: 'Scalar', number: '0.1' },
    },
  });
  const locations: [string, (kind: 'parameter' | 'value') => Partial<StrategyDefinition>][] = [
    [
      'named expression',
      (kind) => ({ values: [{ name: 'calculation', expression: nested(kind) }] }),
    ],
    ...(['tag', 'amount', 'quantity', 'price'] as const).map(
      (setting): [string, (kind: 'parameter' | 'value') => Partial<StrategyDefinition>] => [
        `block ${setting}`,
        (kind) => ({
          blocks: [{ ...order, displayName: 'Покупка после сигнала', [setting]: nested(kind) }],
        }),
      ],
    ),
    [
      'event tag',
      (kind) => ({
        blocks: [
          {
            ...start,
            source: 'OnOrderFilled',
            displayName: 'Исполнение входа',
            tag: reference(kind),
          },
        ],
      }),
    ],
    [
      'cancel tag',
      (kind) => ({
        blocks: [
          { id: 'cancel', kind: 'cancel', displayName: 'Отмена выхода', tag: reference(kind) },
        ],
      }),
    ],
    [
      'check operand',
      (kind) => ({
        blocks: [
          {
            ...condition,
            displayName: 'Можно войти?',
            checks: [{ type: 'Compare', operands: { a: nested(kind) } }],
          },
        ],
      }),
    ],
    [
      'check tag',
      (kind) => ({
        blocks: [
          {
            ...condition,
            displayName: 'Можно войти?',
            checks: [{ type: 'NoActiveTaggedOrder', tag: reference(kind) }],
          },
        ],
      }),
    ],
    [
      'active-order argument',
      (kind) => ({
        blocks: [
          {
            ...order,
            displayName: 'Выход по цене ордера',
            price: { kind: 'activeOrder', field: 'price', arguments: { tag: reference(kind) } },
          },
        ],
      }),
    ],
  ];
  it.each(
    (['parameter', 'value'] as const).flatMap((kind) =>
      locations.map(([label, make]) => [kind, label, make] as const),
    ),
  )('blocks removing a %s used in %s before changing the draft or history', (kind, _, make) => {
    const uses = make(kind);
    const definition: StrategyDefinition = {
      ...initial.definition,
      ...uses,
      parameters:
        kind === 'parameter' ? [{ name: 'shared', valueType: 'Scalar', defaultValue: '10' }] : [],
      values: [
        ...(kind === 'value'
          ? [
              {
                name: 'shared',
                expression: { kind: 'constant', valueType: 'Scalar', number: '10' },
              },
            ]
          : []),
        ...(uses.values ?? []),
      ],
    };
    const draft = { ...initial, definition };
    useEditorStore.getState().load('strategy', 1, draft, true);
    expect(() =>
      useEditorStore
        .getState()
        .execute(kind === 'parameter' ? removeParameter('shared') : removeValue('shared')),
    ).toThrow(`Нельзя удалить ${kind === 'parameter' ? 'параметр' : 'значение'} «shared»`);
    expect(useEditorStore.getState().draft).toBe(draft);
    expect(useEditorStore.getState().past).toEqual([]);
    expect(useEditorStore.getState().dirty).toBe(false);
  });

  it('reports shared values and indirectly affected block display names', () => {
    const draft: Draft = {
      ...initial,
      definition: {
        ...initial.definition,
        parameters: [{ name: 'period', valueType: 'Integer', defaultValue: '20' }],
        values: [
          {
            name: 'average',
            expression: {
              kind: 'SMA',
              arguments: { period: { kind: 'parameter', name: 'period' } },
            },
          },
          {
            name: 'signal',
            expression: { kind: 'previous', arguments: { in: { kind: 'value', name: 'average' } } },
          },
        ],
        blocks: [
          {
            ...order,
            displayName: 'Покупка после пересечения',
            amount: { kind: 'value', name: 'signal' },
          },
        ],
      },
    };
    expect(() => removeParameter('period').apply(draft)).toThrow(
      'значении «average», значении «signal», блоке «Покупка после пересечения»',
    );
    expect(() => removeValue('average').apply(draft)).toThrow(
      'значении «signal», блоке «Покупка после пересечения»',
    );
  });

  it('handles named-value cycles without recursion and blocks external references', () => {
    const draft: Draft = {
      ...initial,
      definition: {
        ...initial.definition,
        values: [
          { name: 'first', expression: { kind: 'value', name: 'second' } },
          { name: 'second', expression: { kind: 'value', name: 'first' } },
        ],
      },
    };
    expect(() => removeValue('first').apply(draft)).toThrow('значении «second»');
    expect(() => removeValue('second').apply(draft)).toThrow('значении «first»');
  });

  it('allows removing a self-reference when no other expression uses it', () => {
    const draft: Draft = {
      ...initial,
      definition: {
        ...initial.definition,
        values: [{ name: 'self', expression: { kind: 'value', name: 'self' } }],
      },
    };
    expect(removeValue('self').apply(draft).definition.values).toEqual([]);
  });

  it.each(['parameter', 'value'] as const)(
    'removes an unused %s in one reversible undo step',
    (kind) => {
      const draft: Draft = {
        ...initial,
        definition: {
          ...initial.definition,
          parameters: [{ name: 'unused', valueType: 'Scalar', defaultValue: '1' }],
          values: [
            { name: 'unused', expression: { kind: 'constant', valueType: 'Scalar', number: '1' } },
          ],
        },
      };
      useEditorStore.getState().load('strategy', 1, draft, true);
      useEditorStore
        .getState()
        .execute(kind === 'parameter' ? removeParameter('unused') : removeValue('unused'));
      const next = useEditorStore.getState().draft;
      expect(useEditorStore.getState().past).toHaveLength(1);
      expect(next!.definition[kind === 'parameter' ? 'parameters' : 'values']).toEqual([]);
      expect(next!.definition[kind === 'parameter' ? 'values' : 'parameters']).toHaveLength(1);
      useEditorStore.getState().undo();
      expect(useEditorStore.getState().draft).toEqual(draft);
      useEditorStore.getState().redo();
      expect(useEditorStore.getState().draft).toEqual(next);
    },
  );
});

it('rejects an invalid insertion atomically without adding a block, selection or undo step', () => {
  useEditorStore.getState().load('s', 1, initial, true);
  expect(() =>
    useEditorStore
      .getState()
      .execute(insertBlock({ nodeId: 'missing', port: 'yes' }, { id: 'new', kind: 'cancel' })),
  ).toThrow();
  expect(useEditorStore.getState().draft).toEqual(initial);
  expect(useEditorStore.getState().past).toHaveLength(0);
  expect(useEditorStore.getState().selectedNodeId).toBeNull();
});
