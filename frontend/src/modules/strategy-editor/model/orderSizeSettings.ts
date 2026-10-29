import type {
  StrategyBlock,
  StrategyCheck,
  StrategyDefinition,
  ValueExpression,
} from '@/modules/strategies';

export type OrderSizeMode = 'amount' | 'quantity';

export function withMinimumOrderPolicy(block: StrategyBlock, policy: string): StrategyBlock {
  const next = { ...block };
  if (policy === 'RaiseToExchangeMinimum' && block.side === 'Buy') {
    next.minimumOrderPolicy = policy;
  } else {
    delete next.minimumOrderPolicy;
  }
  return next;
}

export function orderSizeMode(block: StrategyBlock): OrderSizeMode {
  return block.sizeMode === 'amount' || block.sizeMode === 'quantity'
    ? block.sizeMode
    : block.side === 'Sell'
      ? 'quantity'
      : 'amount';
}

export function needsOrderConversion(block: StrategyBlock): boolean {
  return orderSizeMode(block) === (block.side === 'Sell' ? 'amount' : 'quantity');
}

export function showsOrderPrice(block: StrategyBlock): boolean {
  return block.orderKind === 'Limit' || needsOrderConversion(block);
}

function keepPrice(block: StrategyBlock): StrategyBlock {
  return showsOrderPrice(block) && !block.price
    ? { ...block, price: { kind: 'candle', field: 'Close' } }
    : block;
}

export function withOrderSide(block: StrategyBlock, side: string): StrategyBlock {
  const oldMode = orderSizeMode(block);
  const wholeBase =
    oldMode === 'quantity' &&
    block.quantity?.kind === 'balance' &&
    block.quantity.field === 'baseAvailable';
  const defaultName = block.side === 'Sell' ? 'Продажа' : 'Покупка';
  const defaultLimitName = block.side === 'Sell' ? 'Лимитная продажа' : 'Лимитная покупка';
  const defaultNames = [
    defaultName,
    `${defaultName} по рынку`,
    `${defaultName} по лимиту`,
    `${defaultName} лимит`,
  ];
  const next = keepPrice({
    ...block,
    side,
    sizeMode: side === 'Buy' && wholeBase ? 'amount' : oldMode,
    ...(block.displayName === defaultLimitName
      ? { displayName: side === 'Sell' ? 'Лимитная продажа' : 'Лимитная покупка' }
      : block.displayName && defaultNames.includes(block.displayName)
        ? {
            displayName: block.displayName.replace(
              defaultName,
              side === 'Sell' ? 'Продажа' : 'Покупка',
            ),
          }
        : {}),
  });
  if (side !== 'Buy') {
    delete next.minimumOrderPolicy;
  }
  return next;
}

export function withOrderSizeMode(block: StrategyBlock, sizeMode: OrderSizeMode): StrategyBlock {
  return keepPrice({ ...block, sizeMode });
}

export function withOrderKind(block: StrategyBlock, orderKind: string): StrategyBlock {
  return keepPrice({ ...block, orderKind });
}

export function previewOrder(check: StrategyCheck): StrategyBlock {
  return {
    id: '',
    kind: 'order',
    side: check.side,
    orderKind: check.orderKind,
    sizeMode: check.sizeMode,
    amount: check.operands?.amount,
    quantity: check.operands?.quantity,
    price: check.operands?.price,
    ...(check.minimumOrderPolicy ? { minimumOrderPolicy: check.minimumOrderPolicy } : {}),
  };
}
export function withPreviewSettings(
  check: StrategyCheck,
  settings: Partial<StrategyCheck>,
): StrategyCheck {
  const next = { ...check, ...settings };
  if (next.side === 'Sell' || next.minimumOrderPolicy === 'Reject') {
    delete next.minimumOrderPolicy;
  }
  const order = previewOrder(next);
  const mode = orderSizeMode(order);
  return {
    ...next,
    sizeMode: mode,
    operands: {
      ...check.operands,
      [mode]: check.operands?.[mode] ?? {
        kind: 'constant',
        valueType: mode === 'amount' ? 'QuoteAmount' : 'BaseAmount',
        number: '0',
      },
      ...(showsOrderPrice(order)
        ? { price: check.operands?.price ?? { kind: 'candle', field: 'Close' } }
        : {}),
    },
  };
}

export function startsWithoutCandle(definition: StrategyDefinition, blockId: string): boolean {
  const reached = new Set(
    definition.blocks
      .filter((block) => block.kind === 'event' && block.source === 'OnStart')
      .map((block) => block.id),
  );
  const blocks = new Map(definition.blocks.map((block) => [block.id, block]));
  const pending = [...reached];
  while (pending.length) {
    const id = pending.pop()!;
    if (id === blockId) {
      return true;
    }
    for (const wire of definition.wires) {
      if (wire.from.block !== id || reached.has(wire.to)) {
        continue;
      }
      if (
        blocks.get(id)?.kind === 'order' &&
        ['filled', 'cancelled', 'rejected'].includes(wire.from.exit)
      ) {
        continue;
      }
      reached.add(wire.to);
      pending.push(wire.to);
    }
  }
  return false;
}
export function needsCandle(
  expression: ValueExpression | null | undefined,
  definition: StrategyDefinition,
): boolean {
  const pending = expression ? [expression] : [];
  const seen = new Set<ValueExpression>();
  const values = new Map(definition.values.map((value) => [value.name, value.expression]));
  while (pending.length) {
    const value = pending.pop()!;
    if (seen.has(value)) {
      continue;
    }
    seen.add(value);
    if (
      ['candle', 'SMA', 'ATR', 'EMA', 'RSI', 'Bollinger', 'MACD', 'previous'].includes(value.kind)
    ) {
      return true;
    }
    if (value.kind === 'value' && value.name && values.has(value.name)) {
      pending.push(values.get(value.name)!);
    }
    pending.push(...Object.values(value.arguments ?? {}));
  }
  return false;
}
