import type {
  BlockCatalog,
  StrategyDefinition,
  StrategyTemplate,
  StrategyValue,
  ValueExpression,
} from '@/modules/strategies';
import catalogData from '@/test/blockCatalog.fixture.json';
import templatesData from '@/test/strategyTemplates.fixture.json';

import { checkText, expressionText, presentBlocks } from './blockPresentation';

it.each([
  [
    'Buy',
    'quantity',
    'Кол-во',
    { kind: 'constant', number: '0.5', valueType: 'BaseAmount' },
    '0.5',
  ],
  [
    'Buy',
    'quantity',
    'Кол-во',
    { kind: 'balance', field: 'baseAvailable' },
    'весь доступный актив',
  ],
  ['Sell', 'amount', 'Сумма', { kind: 'constant', number: '50', valueType: 'QuoteAmount' }, '50'],
] as const)('shows approximate %s/%s size on the canvas', (side, sizeMode, label, size, text) => {
  const definition: StrategyDefinition = {
    parameters: [],
    values: [],
    wires: [],
    blocks: [
      {
        id: 'order',
        kind: 'order',
        side,
        sizeMode,
        orderKind: 'Market',
        amount: sizeMode === 'amount' ? size : null,
        quantity: sizeMode === 'quantity' ? size : null,
        price: { kind: 'candle', field: 'Close' },
      },
    ],
  };
  expect(presentBlocks(definition).nodes[0]!.details).toContainEqual([
    label,
    `≈ ${text} по цене сигнала`,
  ]);
});

it('uses event catalog labels on the canvas while retaining custom names', () => {
  const catalog = catalogData as BlockCatalog;
  const definition: StrategyDefinition = {
    parameters: [],
    values: [],
    wires: [],
    blocks: catalog.eventSources.map((entry) => ({
      id: entry.key,
      kind: 'event',
      source: entry.key,
    })),
  };
  expect(presentBlocks(definition, catalog).nodes.map((node) => node.title)).toEqual(
    catalog.eventSources.map((entry) => entry.label),
  );
  expect(
    presentBlocks(
      { ...definition, blocks: [{ ...definition.blocks[0]!, displayName: 'Мой старт' }] },
      catalog,
    ).nodes[0]!.title,
  ).toBe('Мой старт');
});

const templates = templatesData as StrategyTemplate[];
it.each([
  ['SmaCrossover', 5],
  ['Ladder', 13],
])('renders %s stored blocks directly', (kind, count) => {
  const definition = templates.find((template) => template.kind === kind)!.definition;
  const board = presentBlocks(definition);
  expect(board.nodes).toHaveLength(count);
  expect(board.edges).toHaveLength(definition.wires.length);
  expect(board.nodes.map((node) => node.id)).toEqual(definition.blocks.map((block) => block.id));
  expect(board.nodes.every((node) => node.detached === false)).toBe(true);
});
it('formats shared expressions without nodes and terminates cyclic draft values', () => {
  const definition = templates[0]!.definition;
  expect(expressionText({ kind: 'value', name: 'fastSma' }, definition)).toBe('SMA 20');
  const cyclic = {
    ...definition,
    values: [{ name: 'loop', expression: { kind: 'value', name: 'loop' } }],
  };
  expect(expressionText({ kind: 'value', name: 'loop' }, cyclic)).toBe('loop');
  expect(
    checkText(
      {
        type: 'CrossesAbove',
        operands: { a: { kind: 'value', name: 'fastSma' }, b: { kind: 'value', name: 'slowSma' } },
      },
      definition,
    ),
  ).toBe('SMA 20 ↗ SMA 50');
});
it('keeps unwired blocks visible and marks them detached', () => {
  const definition = templates[0]!.definition;
  const board = presentBlocks({
    ...definition,
    blocks: [...definition.blocks, { id: 'unused', kind: 'cancel' }],
  });
  expect(board.nodes.find((block) => block.id === 'unused')?.detached).toBe(true);
});

it('bounds shared expression work and label length for forty doubling values', () => {
  let reads = 0;
  const values: StrategyValue[] = [
    { name: 'v0', expression: { kind: 'constant', valueType: 'Scalar', number: '1' } },
  ];
  for (let index = 1; index < 40; index++) {
    const previous = { kind: 'value', name: `v${index - 1}` };
    const expression: ValueExpression = { kind: 'arithmetic', operation: 'Add' };
    Object.defineProperty(expression, 'arguments', {
      get: () => {
        if (++reads > 200) {
          throw new Error('Shared values were expanded repeatedly');
        }
        return { a: previous, b: previous };
      },
    });
    values.push({ name: `v${index}`, expression });
  }
  const text = expressionText(
    { kind: 'value', name: 'v39' },
    { ...templates[0]!.definition, values },
  );
  expect(text.length).toBeLessThanOrEqual(160);
  expect(text).toContain('…');
  expect(reads).toBeLessThanOrEqual(200);
});

it('shows the remainder operation as mod', () => {
  const definition: StrategyDefinition = {
    parameters: [{ name: 'interval', valueType: 'Integer', defaultValue: '3' }],
    values: [],
    wires: [],
    blocks: [],
  };
  const expression: ValueExpression = {
    kind: 'arithmetic',
    operation: 'Modulo',
    arguments: {
      a: { kind: 'position', field: 'barsHeld' },
      b: { kind: 'parameter', name: 'interval' },
    },
  };
  expect(expressionText(expression, definition)).toContain(' mod ');
});

it('keeps a deeply nested invalid draft renderable', () => {
  let expression: ValueExpression = { kind: 'constant', valueType: 'Scalar', number: '1' };
  for (let index = 0; index < 2000; index++) {
    expression = { kind: 'previous', arguments: { in: expression } };
  }
  expect(expressionText(expression, templates[0]!.definition).length).toBeLessThanOrEqual(160);
});

it.each([
  ['Buy', 'quantity', 'Кол-во', '1.234567890123456789'],
  ['Sell', 'amount', 'Сумма', '9007199254740993.123456789'],
] as const)(
  'shows the chosen %s/%s size on the canvas independently of side',
  (side, sizeMode, label, value) => {
    const definition = {
      ...templates[0]!.definition,
      blocks: [
        {
          id: 'sized',
          kind: 'order',
          side,
          sizeMode,
          orderKind: 'Market',
          amount: {
            kind: 'constant',
            valueType: 'QuoteAmount',
            number: '9007199254740993.123456789',
          },
          quantity: { kind: 'constant', valueType: 'BaseAmount', number: '1.234567890123456789' },
          price: { kind: 'candle', field: 'Close' },
        },
      ],
    };
    const details = presentBlocks(definition).nodes[0]!.details;
    expect(details).toContainEqual([label, `≈ ${value} по цене сигнала`]);
    expect(details).toContainEqual(['Цена пересчёта', expect.any(String)]);
    expect(details.some(([key]) => key === (label === 'Сумма' ? 'Кол-во' : 'Сумма'))).toBe(false);
  },
);
it('hides a preserved conversion price on a native market order', () => {
  const definition = {
    ...templates[0]!.definition,
    blocks: [
      {
        id: 'sized',
        kind: 'order',
        side: 'Buy',
        sizeMode: 'amount',
        orderKind: 'Market',
        amount: { kind: 'constant', valueType: 'QuoteAmount', number: '100' },
        price: { kind: 'candle', field: 'Close' },
      },
    ],
  };
  expect(
    presentBlocks(definition).nodes[0]!.details.some(
      ([key]) => key === 'Цена' || key === 'Цена пересчёта',
    ),
  ).toBe(false);
});

it('shows percent parameters in canvas formulas while unit-less values stay raw', () => {
  const definition: StrategyDefinition = {
    templateKind: 'Ladder',
    parameters: [
      { name: 'drop', valueType: 'Scalar', defaultValue: '0.1', displayUnit: 'percent' },
    ],
    values: [],
    blocks: [],
    wires: [],
  };
  expect(expressionText({ kind: 'parameter', name: 'drop' }, definition)).toBe('10%');
  expect(
    expressionText(
      { kind: 'parameter', name: 'drop' },
      { ...definition, parameters: [{ name: 'drop', valueType: 'Scalar', defaultValue: '0.1' }] },
    ),
  ).toBe('0.1');
});
