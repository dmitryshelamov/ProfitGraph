import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react';

import type { BlockCatalog, StrategyDiagnostic, StrategyTemplate } from '@/modules/strategies';
import catalogData from '@/test/blockCatalog.fixture.json';
import templateData from '@/test/strategyTemplates.fixture.json';

import { useEditorStore } from '../model/editorStore';
import { initialGridSettings } from '../model/gridSettings';
import { FlowchartInspector } from './FlowchartInspector';
import { StrategyValuesPanel } from './StrategyValuesPanel';

const catalog = catalogData as unknown as BlockCatalog;
const parityCatalog: BlockCatalog = {
  ...catalog,
  orderKinds: catalog.orderKinds.map((entry) => ({
    ...entry,
    minimumOrderPolicies: ['Reject', 'RaiseToExchangeMinimum'],
  })),
  checkTypes: catalog.checkTypes.map((entry) =>
    entry.key === 'OrderSizePreviewValid'
      ? {
          ...entry,
          minimumOrderPolicies: ['Reject', 'RaiseToExchangeMinimum'],
          operands: [
            ...entry.operands,
            {
              key: 'quoteLimit',
              label: 'Оставшийся депозит',
              required: false,
              valueTypes: ['QuoteAmount'],
            },
          ],
        }
      : entry,
  ),
};
const templates = templateData as unknown as StrategyTemplate[];
const sma = templates.find((template) => template.kind === 'SmaCrossover')!;

function Inspector({
  blockId = 'buySignal',
  diagnostics = [],
  blockCatalog = catalog,
}: {
  blockId?: string;
  diagnostics?: StrategyDiagnostic[];
  blockCatalog?: BlockCatalog;
}) {
  const draft = useEditorStore((state) => state.draft)!;
  return (
    <FlowchartInspector
      block={draft.definition.blocks.find((block) => block.id === blockId)!}
      definition={draft.definition}
      catalog={blockCatalog}
      diagnostics={diagnostics}
      fieldErrors={{}}
    />
  );
}
function Values() {
  const draft = useEditorStore((state) => state.draft)!;
  return <StrategyValuesPanel definition={draft.definition} catalog={catalog} diagnostics={[]} />;
}

// Opens every collapsed order setting that is on screen.
const expand = () =>
  screen
    .queryAllByRole('button', { name: /^Изменить: / })
    .forEach((button) => fireEvent.click(button));
const radios = (group: string) =>
  within(screen.getByRole('radiogroup', { name: group })).getAllByRole<HTMLInputElement>('radio');
const choose = (group: string, value: string) => {
  fireEvent.click(radios(group).find((radio) => radio.value === value)!);
  expand();
};
const chosen = (group: string) => radios(group).find((radio) => radio.checked)?.value;
const openCheck = (index: number) =>
  fireEvent.click(screen.getByRole('button', { name: `Изменить проверку ${index}` }));

beforeEach(() =>
  useEditorStore
    .getState()
    .load('strategy', 1, { name: 'SMA', definition: sma.definition, layout: sma.layout }, true),
);
afterEach(cleanup);

it('edits the buy minimum policy without storing the default and supports undo and reload', () => {
  render(<Inspector blockId="buy" blockCatalog={parityCatalog} />);
  const selected = () =>
    useEditorStore.getState().draft!.definition.blocks.find((block) => block.id === 'buy')!;
  expect(chosen('Минимальный размер ордера')).toBe('Reject');
  expect(selected()).not.toHaveProperty('minimumOrderPolicy');
  choose('Минимальный размер ордера', 'RaiseToExchangeMinimum');
  expect(selected().minimumOrderPolicy).toBe('RaiseToExchangeMinimum');
  expect(
    screen.getByText(/Для инструмента с целыми лотами может увеличить покупку до одного лота/),
  ).toBeInTheDocument();
  choose('Минимальный размер ордера', 'Reject');
  expect(selected()).not.toHaveProperty('minimumOrderPolicy');
  act(() => useEditorStore.getState().undo());
  expect(chosen('Минимальный размер ордера')).toBe('RaiseToExchangeMinimum');
  const reloaded = JSON.parse(JSON.stringify(useEditorStore.getState().draft!));
  act(() => useEditorStore.getState().load('strategy', 2, reloaded, true));
  expect(chosen('Минимальный размер ордера')).toBe('RaiseToExchangeMinimum');
  choose('Сторона ордера', 'Sell');
  expect(
    screen.queryByRole('radiogroup', { name: 'Минимальный размер ордера' }),
  ).not.toBeInTheDocument();
  expect(selected()).not.toHaveProperty('minimumOrderPolicy');
  act(() => useEditorStore.getState().undo());
  expect(chosen('Минимальный размер ордера')).toBe('RaiseToExchangeMinimum');
});

it('edits the buy preview policy and its exact remaining-deposit limit', () => {
  const condition = {
    id: 'guard',
    kind: 'condition',
    checks: [
      {
        type: 'OrderSizePreviewValid',
        side: 'Buy',
        orderKind: 'Limit',
        operands: {
          amount: { kind: 'constant', valueType: 'QuoteAmount', number: '4' },
          price: { kind: 'constant', valueType: 'Price', number: '9' },
          quoteLimit: {
            kind: 'constant',
            valueType: 'QuoteAmount',
            number: '5.123456789012345678',
          },
        },
      },
    ],
  };
  useEditorStore
    .getState()
    .load(
      'strategy',
      1,
      { name: 'Guard', definition: { ...sma.definition, blocks: [condition] }, layout: sma.layout },
      true,
    );
  render(<Inspector blockId="guard" blockCatalog={parityCatalog} />);
  openCheck(1);
  expect(screen.getByLabelText('Значение Проверка 1: Оставшийся депозит')).toHaveValue(
    '5.123456789012345678',
  );
  choose('Минимальный размер проверки 1', 'RaiseToExchangeMinimum');
  const selected = () => useEditorStore.getState().draft!.definition.blocks[0]!.checks![0]!;
  expect(selected().minimumOrderPolicy).toBe('RaiseToExchangeMinimum');
  choose('Минимальный размер проверки 1', 'Reject');
  expect(selected()).not.toHaveProperty('minimumOrderPolicy');
  expect(selected().operands!.quoteLimit!.number).toBe('5.123456789012345678');
  choose('Сторона проверки 1', 'Sell');
  expect(
    screen.queryByRole('radiogroup', { name: 'Минимальный размер проверки 1' }),
  ).not.toBeInTheDocument();
});

it('edits the ordered check list directly and each operation is undoable', () => {
  render(<Inspector />);
  fireEvent.change(screen.getByLabelText('Объединение условий'), { target: { value: 'any' } });
  expect(
    useEditorStore.getState().draft!.definition.blocks.find((block) => block.id === 'buySignal')!
      .join,
  ).toBe('any');
  act(() => useEditorStore.getState().undo());
  expect(screen.getByLabelText('Объединение условий')).toHaveValue('all');
  openCheck(2);
  fireEvent.click(screen.getByRole('button', { name: 'Поднять проверку 2' }));
  expect(
    useEditorStore.getState().draft!.definition.blocks.find((block) => block.id === 'buySignal')!
      .checks![0]!.type,
  ).toBe('PositionFlat');
  expect(screen.getByRole('button', { name: 'Удалить проверку 1' })).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Удалить проверку 1' }));
  expect(
    useEditorStore.getState().draft!.definition.blocks.find((block) => block.id === 'buySignal')!
      .checks,
  ).toHaveLength(2);
  act(() => useEditorStore.getState().undo());
  expect(
    useEditorStore.getState().draft!.definition.blocks.find((block) => block.id === 'buySignal')!
      .checks,
  ).toHaveLength(3);
  fireEvent.click(screen.getByRole('button', { name: '+ Проверка' }));
  fireEvent.click(screen.getByRole('menuitem', { name: 'Позиция закрыта' }));
  expect(
    useEditorStore.getState().draft!.definition.blocks.find((block) => block.id === 'buySignal')!
      .checks,
  ).toHaveLength(4);
  expect(useEditorStore.getState().draft!.definition.blocks).toHaveLength(5);
});

it('binds a check operand to a parameter without creating graph nodes', () => {
  render(<Inspector />);
  openCheck(1);
  fireEvent.change(screen.getByLabelText('Источник Проверка 1: Первое значение'), {
    target: { value: 'parameter' },
  });
  fireEvent.change(screen.getByLabelText('Ссылка Проверка 1: Первое значение'), {
    target: { value: 'buyAmount' },
  });
  const definition = useEditorStore.getState().draft!.definition;
  expect(
    definition.blocks.find((block) => block.id === 'buySignal')!.checks![0]!.operands!.a,
  ).toEqual({ kind: 'parameter', name: 'buyAmount' });
  expect(definition.parameters).toHaveLength(3);
  expect(definition.values).toHaveLength(3);
});

it('updates a shared parameter as an exact string in one typing undo step', () => {
  render(<Values />);
  const input = screen.getByLabelText('Значение параметра buyAmount');
  fireEvent.change(input, { target: { value: '9007199254740993.123456789' } });
  fireEvent.blur(input);
  expect(
    useEditorStore
      .getState()
      .draft!.definition.parameters.find((parameter) => parameter.name === 'buyAmount')!
      .defaultValue,
  ).toBe('9007199254740993.123456789');
  expect(useEditorStore.getState().past).toHaveLength(1);
  act(() => useEditorStore.getState().undo());
  expect(input).toHaveValue('100');
});

it('adds a named value and rejects collisions across parameters and values', () => {
  render(<Values />);
  fireEvent.change(screen.getByLabelText('Тип именованного значения'), {
    target: { value: 'value' },
  });
  fireEvent.change(screen.getByLabelText('Имя нового значения'), {
    target: { value: 'fastPeriod' },
  });
  fireEvent.click(screen.getByRole('button', { name: 'Добавить значение' }));
  expect(screen.getByRole('alert')).toHaveTextContent('уникальное имя');
  fireEvent.change(screen.getByLabelText('Имя нового значения'), {
    target: { value: 'threshold' },
  });
  fireEvent.click(screen.getByRole('button', { name: 'Добавить значение' }));
  expect(
    useEditorStore.getState().draft!.definition.values.find((value) => value.name === 'threshold')!
      .expression,
  ).toEqual({ kind: 'constant', valueType: 'Scalar', number: '0' });
  act(() => useEditorStore.getState().undo());
  expect(
    screen.queryByRole('group', { name: 'Именованное значение threshold' }),
  ).not.toBeInTheDocument();
});

it('keeps dropdown operand changes as separate undo steps', () => {
  render(<Inspector />);
  openCheck(1);
  fireEvent.change(screen.getByLabelText('Источник Проверка 1: Первое значение'), {
    target: { value: 'parameter' },
  });
  fireEvent.change(screen.getByLabelText('Ссылка Проверка 1: Первое значение'), {
    target: { value: 'buyAmount' },
  });
  expect(useEditorStore.getState().past).toHaveLength(2);
  act(() => useEditorStore.getState().undo());
  expect(screen.getByLabelText('Ссылка Проверка 1: Первое значение')).toHaveValue('fastPeriod');
  act(() => useEditorStore.getState().undo());
  expect(screen.getByLabelText('Источник Проверка 1: Первое значение')).toHaveValue('value');
});
it('allows removing an order-event tag filter and clears it when changing source', () => {
  const definition = {
    ...sma.definition,
    blocks: [
      {
        id: 'event',
        kind: 'event',
        source: 'OnOrderFilled',
        tag: { kind: 'constant', valueType: 'Tag', text: 'entry' },
      },
    ],
  };
  useEditorStore
    .getState()
    .load('strategy', 1, { name: 'Events', definition, layout: sma.layout }, true);
  render(<Inspector blockId="event" />);
  fireEvent.click(screen.getByRole('button', { name: 'Все метки' }));
  expect(useEditorStore.getState().draft!.definition.blocks[0]!.tag).toBeNull();
  act(() => useEditorStore.getState().undo());
  choose('Источник события', 'OnStart');
  expect(useEditorStore.getState().draft!.definition.blocks[0]!.tag).toBeNull();
});
it('initializes a limit preview price when changing from market', () => {
  const definition = {
    ...sma.definition,
    blocks: [
      {
        id: 'preview',
        kind: 'condition',
        join: 'all',
        checks: [
          {
            type: 'OrderSizePreviewValid',
            orderKind: 'Market',
            side: 'Buy',
            operands: { amount: { kind: 'constant', valueType: 'QuoteAmount', number: '100' } },
          },
        ],
      },
    ],
  };
  useEditorStore
    .getState()
    .load('strategy', 1, { name: 'Preview', definition, layout: sma.layout }, true);
  render(<Inspector blockId="preview" />);
  openCheck(1);
  choose('Тип ордера проверки 1', 'Limit');
  expect(
    useEditorStore.getState().draft!.definition.blocks[0]!.checks![0]!.operands!.price,
  ).toEqual({ kind: 'candle', field: 'Close' });
});

it('preserves exact amount, quantity and price across side, size-mode and order-kind edits, each with one undo step', () => {
  const order = {
    id: 'sized',
    kind: 'order',
    side: 'Buy',
    orderKind: 'Limit',
    amount: { kind: 'constant', valueType: 'QuoteAmount', number: '9007199254740993.123456789' },
    quantity: {
      kind: 'constant',
      valueType: 'BaseAmount',
      number: '0.0000000000000000000000000001',
    },
    price: { kind: 'constant', valueType: 'Price', number: '123.123456789012345678' },
  };
  useEditorStore
    .getState()
    .load(
      'strategy',
      1,
      { name: 'Order', definition: { ...sma.definition, blocks: [order] }, layout: sma.layout },
      true,
    );
  render(<Inspector blockId="sized" />);
  expand();
  const selected = () => useEditorStore.getState().draft!.definition.blocks[0]!;
  expect(chosen('Размер ордера')).toBe('amount');
  choose('Сторона ордера', 'Sell');
  expect(chosen('Размер ордера')).toBe('amount');
  expect(screen.getByLabelText('Значение Сумма')).toHaveValue(order.amount.number);
  expect(useEditorStore.getState().past).toHaveLength(1);
  choose('Размер ордера', 'quantity');
  expect(screen.getByLabelText('Значение Количество')).toHaveValue(order.quantity.number);
  expect(useEditorStore.getState().past).toHaveLength(2);
  choose('Тип ордера', 'Market');
  expect(screen.queryByLabelText('Источник Цена')).not.toBeInTheDocument();
  expect(screen.queryByLabelText('Источник Цена пересчёта')).not.toBeInTheDocument();
  expect(useEditorStore.getState().past).toHaveLength(3);
  choose('Размер ордера', 'amount');
  expect(screen.getByLabelText('Значение Цена пересчёта')).toHaveValue(order.price.number);
  expect(screen.getByText(/в момент сигнала/)).toBeInTheDocument();
  expect(useEditorStore.getState().past).toHaveLength(4);
  expect(selected().amount).toEqual(order.amount);
  expect(selected().quantity).toEqual(order.quantity);
  expect(selected().price).toEqual(order.price);
  act(() => useEditorStore.getState().undo());
  expect(chosen('Размер ордера')).toBe('quantity');
  expect(selected().price).toEqual(order.price);
  act(() => useEditorStore.getState().redo());
  const reloaded = JSON.parse(JSON.stringify(useEditorStore.getState().draft!));
  act(() => useEditorStore.getState().load('strategy', 2, reloaded, true));
  expect(chosen('Сторона ордера')).toBe('Sell');
  expect(chosen('Размер ордера')).toBe('amount');
  expect(screen.getByLabelText('Значение Сумма')).toHaveValue(order.amount.number);
  expect(screen.getByLabelText('Значение Цена пересчёта')).toHaveValue(order.price.number);
});

it('supports buy quantity and sell amount without manufacturing an unset size, with an explicit market conversion price', () => {
  const order = {
    id: 'sized',
    kind: 'order',
    side: 'Buy',
    orderKind: 'Market',
    amount: { kind: 'constant', valueType: 'QuoteAmount', number: '100' },
  };
  useEditorStore
    .getState()
    .load(
      'strategy',
      1,
      { name: 'Order', definition: { ...sma.definition, blocks: [order] }, layout: sma.layout },
      true,
    );
  render(<Inspector blockId="sized" />);
  expand();
  choose('Размер ордера', 'quantity');
  expect(screen.getByLabelText('Источник Количество')).toHaveValue('');
  expect(screen.getByLabelText('Источник Цена пересчёта')).toHaveValue('candle');
  expect(useEditorStore.getState().draft!.definition.blocks[0]!.quantity).toBeUndefined();
  expect(useEditorStore.getState().draft!.definition.blocks[0]!.amount).toEqual(order.amount);
  fireEvent.change(screen.getByLabelText('Источник Количество'), { target: { value: 'constant' } });
  fireEvent.change(screen.getByLabelText('Значение Количество'), {
    target: { value: '1.234567890123456789' },
  });
  fireEvent.blur(screen.getByLabelText('Значение Количество'));
  choose('Сторона ордера', 'Sell');
  expect(chosen('Размер ордера')).toBe('quantity');
  choose('Размер ордера', 'amount');
  expect(screen.getByLabelText('Значение Сумма')).toHaveValue('100');
  expect(useEditorStore.getState().draft!.definition.blocks[0]!.quantity!.number).toBe(
    '1.234567890123456789',
  );
});

it('preserves both entered preview size operands when changing side', () => {
  const amount = {
    kind: 'constant',
    valueType: 'QuoteAmount',
    number: '9007199254740993.123456789',
  };
  const quantity = {
    kind: 'constant',
    valueType: 'BaseAmount',
    number: '0.0000000000000000000000000001',
  };
  const definition = {
    ...sma.definition,
    blocks: [
      {
        id: 'preview',
        kind: 'condition',
        join: 'all',
        checks: [
          {
            type: 'OrderSizePreviewValid',
            orderKind: 'Market',
            side: 'Buy',
            operands: { amount, quantity },
          },
        ],
      },
    ],
  };
  useEditorStore
    .getState()
    .load('strategy', 1, { name: 'Preview', definition, layout: sma.layout }, true);
  render(<Inspector blockId="preview" />);
  openCheck(1);
  choose('Сторона проверки 1', 'Sell');
  choose('Размер проверки 1', 'quantity');
  expect(screen.getByLabelText('Значение Проверка 1: Количество')).toHaveValue(quantity.number);
  choose('Сторона проверки 1', 'Buy');
  choose('Размер проверки 1', 'amount');
  expect(screen.getByLabelText('Значение Проверка 1: Сумма')).toHaveValue(amount.number);
  expect(useEditorStore.getState().draft!.definition.blocks[0]!.checks![0]!.operands).toEqual({
    amount,
    quantity,
    price: { kind: 'candle', field: 'Close' },
  });
});

it('explains that inverse limit size can change after existing price normalization', () => {
  const order = {
    id: 'sized',
    kind: 'order',
    side: 'Buy',
    sizeMode: 'quantity',
    orderKind: 'Limit',
    quantity: { kind: 'constant', valueType: 'BaseAmount', number: '1' },
    price: { kind: 'constant', valueType: 'Price', number: '100.09' },
  };
  useEditorStore
    .getState()
    .load(
      'strategy',
      1,
      { name: 'Order', definition: { ...sma.definition, blocks: [order] }, layout: sma.layout },
      true,
    );
  render(<Inspector blockId="sized" />);
  expand();
  expect(screen.getByLabelText('Значение Количество')).toHaveValue('1');
  expect(screen.getByLabelText('Значение Цена')).toHaveValue('100.09');
  expect(
    screen.getByText(
      /После округления цены и размера фактическая сумма или количество могут отличаться/,
    ),
  ).toBeInTheDocument();
});

it('warns when a start path reaches inverse market sizing before a candle exists and exposes remembered references for explicit clearing', () => {
  const definition = {
    ...sma.definition,
    blocks: [
      { id: 'start', kind: 'event', source: 'OnStart' },
      { id: 'decision', kind: 'condition', checks: [] },
      {
        id: 'sized',
        kind: 'order',
        side: 'Buy',
        sizeMode: 'quantity',
        orderKind: 'Market',
        quantity: { kind: 'constant', valueType: 'BaseAmount', number: '1.000000000000000001' },
        amount: { kind: 'parameter', name: 'buyAmount' },
        price: { kind: 'candle', field: 'Close' },
      },
    ],
    wires: [
      { from: { block: 'start', exit: 'out' }, to: 'decision' },
      { from: { block: 'decision', exit: 'yes' }, to: 'sized' },
    ],
  };
  useEditorStore
    .getState()
    .load('strategy', 1, { name: 'Startup', definition, layout: sma.layout }, true);
  render(<Inspector blockId="sized" />);
  expand();
  expect(screen.getByRole('alert')).toHaveTextContent('закрытая свеча ещё недоступна');
  expect(screen.getByLabelText('Ссылка Сохранённое Сумма')).toHaveValue('buyAmount');
  fireEvent.click(screen.getByRole('button', { name: 'Очистить сохранённое сумму' }));
  expect(useEditorStore.getState().draft!.definition.blocks[2]!.amount).toBeNull();
  expect(useEditorStore.getState().draft!.definition.blocks[2]!.quantity!.number).toBe(
    '1.000000000000000001',
  );
  act(() => useEditorStore.getState().undo());
  expect(screen.getByLabelText('Ссылка Сохранённое Сумма')).toHaveValue('buyAmount');
});

it('switches the template whole-base sale to buy amount without erasing the remembered quantity or changing custom names', () => {
  render(<Inspector blockId="sell" />);
  expand();
  choose('Сторона ордера', 'Buy');
  expect(chosen('Размер ордера')).toBe('amount');
  expect(screen.getByLabelText('Источник Сумма')).toHaveValue('');
  expect(screen.getByLabelText('Поле Сохранённое Количество')).toHaveValue('baseAvailable');
  expect(screen.getByLabelText('Название блока')).toHaveValue('Покупка по рынку');
});

it('locates diagnostics on the ordered check row without an opaque execution ID', () => {
  render(
    <Inspector
      diagnostics={[
        {
          code: 'invalidOperand',
          message: 'Введите доступное значение.',
          blockId: 'buySignal',
          checkIndex: 1,
          valueName: null,
          parameterName: null,
          setting: 'amount',
        },
      ]}
    />,
  );
  expect(screen.getByRole('alert')).toHaveTextContent('Проверка 2: Введите доступное значение.');
  expect(screen.getByRole('group', { name: 'Проверка 2' })).toHaveClass('border-danger-border');
  expect(screen.getByRole('group', { name: 'Проверка 1' })).not.toHaveClass('border-danger-border');
});

it('updates only the default event title when the event source changes', () => {
  const startLabel = catalog.eventSources.find((source) => source.key === 'OnStart')!.label;
  const candleLabel = catalog.eventSources.find((source) => source.key === 'OnCandleClosed')!.label;
  const definition = {
    ...sma.definition,
    blocks: [{ id: 'clock', kind: 'event', source: 'OnStart', displayName: 'Начало прогона' }],
  };
  useEditorStore
    .getState()
    .load('strategy', 1, { name: 'Events', definition, layout: sma.layout }, true);
  render(<Inspector blockId="clock" />);
  choose('Источник события', 'OnCandleClosed');
  expect(screen.getByLabelText('Название блока')).toHaveValue(candleLabel);
  fireEvent.change(screen.getByLabelText('Название блока'), { target: { value: 'Мой сигнал' } });
  choose('Источник события', 'OnStart');
  expect(screen.getByLabelText('Название блока')).toHaveValue('Мой сигнал');
  expect(useEditorStore.getState().draft!.definition.blocks[0]!.source).toBe('OnStart');
  expect(startLabel).not.toBe(candleLabel);
});

it('offers OnResume with its live reason and backtest behavior', () => {
  const definition = {
    ...sma.definition,
    blocks: [{ id: 'clock', kind: 'event', source: 'OnStart' }],
  };
  useEditorStore
    .getState()
    .load('strategy', 1, { name: 'Events', definition, layout: sma.layout }, true);
  render(<Inspector blockId="clock" />);
  choose('Источник события', 'OnResume');
  expect(useEditorStore.getState().draft!.definition.blocks[0]!.source).toBe('OnResume');
  expect(screen.getByText(/причину: перезапуск или смена графа/)).toBeInTheDocument();
});

it('shows what enters the block, what it computes and checks, and where each exit leads', () => {
  render(<Inspector />);
  const input = screen.getByRole('region', { name: 'Вход' });
  expect(within(input).getByRole('button', { name: /Свеча закрылась/ })).toBeInTheDocument();
  const computes = screen.getByRole('group', { name: 'Считает' });
  expect(within(computes).getByText('fastSma')).toBeInTheDocument();
  expect(within(computes).getByText('fastPeriod 20')).toBeInTheDocument();
  // Values come before the values built from them.
  const names = within(computes)
    .getAllByText(/^(close|fastSma|slowSma)$/)
    .filter((item) => item.classList.contains('is-value'))
    .map((item) => item.textContent);
  expect(names.indexOf('close')).toBeLessThan(names.indexOf('fastSma'));
  const checks = screen.getByRole('group', { name: 'Проверка 1' });
  expect(checks).toHaveTextContent('fastSma пересекает снизу вверх slowSma');
  expect(screen.queryByLabelText('Источник Проверка 1: Первое значение')).not.toBeInTheDocument();
  const yes = screen.getByRole('group', { name: 'Выход да' });
  expect(within(yes).getByRole('button', { name: /Покупка по рынку/ })).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Подключить блок к выходу «нет»' }));
  expect(useEditorStore.getState().picker).toEqual({ nodeId: 'buySignal', port: 'no' });
  fireEvent.click(within(input).getByRole('button', { name: /Свеча закрылась/ }));
  expect(useEditorStore.getState().selectedNodeId).toBe('clock');
});

it('edits a shared named value in place and names the other blocks that use it', () => {
  render(<Inspector />);
  fireEvent.click(screen.getByRole('button', { name: 'Изменить значение close' }));
  expect(screen.getByText(/изменится и в блоке «Сигнал на продажу»/)).toBeInTheDocument();
  fireEvent.change(screen.getByLabelText('Поле close'), { target: { value: 'Open' } });
  expect(
    useEditorStore.getState().draft!.definition.values.find((value) => value.name === 'close')!
      .expression.field,
  ).toBe('Open');
});

it('lists the orders that set the tag a cancel block removes', () => {
  const ladder = templates.find((template) => template.kind === 'Ladder')!;
  useEditorStore
    .getState()
    .load(
      'strategy',
      1,
      { name: 'Ladder', definition: ladder.definition, layout: ladder.layout },
      true,
    );
  render(<Inspector blockId="cancelExit" />);
  const cancels = screen.getByRole('group', { name: 'Что отменяет' });
  expect(within(cancels).getByRole('button', { name: /Лимитная продажа/ })).toBeInTheDocument();
  expect(screen.getByRole('region', { name: 'Вход' })).toHaveTextContent('Достаточно любого');
  expect(screen.getByText(/ещё 2 блока с таким именем/)).toBeInTheDocument();
});

it('describes an event as the start of a chain', () => {
  render(<Inspector blockId="clock" />);
  expect(screen.getByRole('region', { name: 'Вход' })).toHaveTextContent('Ничего не входит');
  expect(chosen('Источник события')).toBe('OnCandleClosed');
  const out = screen.getByRole('group', { name: 'Выход запускает' });
  expect(within(out).getAllByRole('button', { name: /Сигнал на/ })).toHaveLength(2);
});

it('shows order settings as short rows with a plain summary', () => {
  render(<Inspector blockId="buy" />);
  const order = screen.getByRole('group', { name: 'Выставляет' });
  expect(within(order).getAllByText('buyAmount 100').length).toBeGreaterThan(0);
  expect(screen.queryByLabelText('Источник Сумма')).not.toBeInTheDocument();
  expect(order).toHaveTextContent('Купить на buyAmount 100 по рынку.');
  fireEvent.click(screen.getByRole('button', { name: 'Изменить: Сумма' }));
  expect(screen.getByLabelText('Ссылка Сумма')).toHaveValue('buyAmount');
});

describe('grid block', () => {
  const gridDefinition = () => ({
    ...sma.definition,
    blocks: [
      ...sma.definition.blocks,
      { id: 'grid', kind: 'grid', settings: initialGridSettings() },
      { id: 'stop', kind: 'cancel', tag: { kind: 'constant', valueType: 'Tag', text: 'buy' } },
    ],
  });
  const block = (id: string) =>
    useEditorStore.getState().draft!.definition.blocks.find((item) => item.id === id)!;

  beforeEach(() =>
    useEditorStore
      .getState()
      .load(
        'strategy',
        1,
        { name: 'Grid', definition: gridDefinition(), layout: sma.layout },
        true,
      ),
  );

  it('edits every grid setting as a value', () => {
    render(<Inspector blockId="grid" />);
    expect(screen.getByRole('group', { name: 'Настройки сетки' })).toBeInTheDocument();
    const levels = screen.getByRole('textbox', { name: 'Значение Ступеней' });
    expect(levels).toHaveValue('10');
    fireEvent.change(levels, { target: { value: '25' } });
    expect(block('grid').settings?.levels).toMatchObject({ kind: 'constant', number: '25' });
    expect(screen.getByRole('group', { name: 'Выход запущена' })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Выход остановлена' })).toBeInTheDocument();
  });

  it('turns a cancel block into a grid stop and back', () => {
    render(<Inspector blockId="stop" />);
    fireEvent.click(radios('Что отменяет').find((radio) => radio.value === 'grid')!);
    expect(block('stop')).toMatchObject({ grid: 'grid' });
    expect(block('stop').tag).toBeUndefined();
    expect(screen.getByText(/останавливает её без продажи/)).toBeInTheDocument();
    fireEvent.click(radios('Что отменяет').find((radio) => radio.value === 'tag')!);
    expect(block('stop').grid).toBeUndefined();
  });
});
