import type {
  BlockCatalog,
  StrategyBlock,
  StrategyCheck,
  StrategyDefinition,
  StrategyWire,
  ValueExpression,
} from '@/modules/strategies';
import { fieldLabel, nodeTypeTitle, parameterValueDisplay } from '@/modules/strategies';

import { referencedValues } from './namedSettingEdits';
import { orderSizeMode, previewOrder, showsOrderPrice } from './orderSizeSettings';

/** A piece of a formula: plain text, a parameter, a named value or a market source. */
export interface FormulaToken {
  readonly kind: 'text' | 'parameter' | 'value' | 'source';
  readonly text: string;
}

const operators: Record<string, string> = {
  Add: '+',
  Subtract: '−',
  Multiply: '×',
  Divide: '÷',
  Modulo: 'mod',
  '<': '<',
  '<=': '≤',
  '=': '=',
  '!=': '≠',
  '>=': '≥',
  '>': '>',
};
const sources: Record<string, string> = {
  candle: 'свеча',
  balance: 'баланс',
  position: 'позиция',
  activeOrder: 'ордер',
};
const lower = (text: string) => text.charAt(0).toLowerCase() + text.slice(1);

function merge(tokens: readonly FormulaToken[]): FormulaToken[] {
  const result: FormulaToken[] = [];
  for (const token of tokens) {
    const last = result.at(-1);
    if (token.kind === 'text' && last?.kind === 'text') {
      result[result.length - 1] = { kind: 'text', text: `${last.text} ${token.text}` };
    } else {
      result.push(token);
    }
  }
  return result;
}

/**
 * Shows one level of a formula: named values stay names and parameters show their current value,
 * so the reader sees what the block uses without the whole tree unfolded.
 */
export function formulaTokens(
  expression: ValueExpression | null | undefined,
  definition: StrategyDefinition,
  catalog?: BlockCatalog,
): FormulaToken[] {
  const parameters = new Map(definition.parameters.map((parameter) => [parameter.name, parameter]));
  const format = (current: ValueExpression | null | undefined, depth: number): FormulaToken[] => {
    if (!current) {
      return [{ kind: 'text', text: '?' }];
    }
    if (depth > 32) {
      return [{ kind: 'text', text: '…' }];
    }
    const input = (key: string) => format(current.arguments?.[key], depth + 1);
    const nested = (key: string) => {
      const operand = current.arguments?.[key];
      const tokens = input(key);
      return operand?.kind === 'arithmetic' && !['Min', 'Max'].includes(operand.operation ?? '')
        ? [{ kind: 'text', text: '(' } as const, ...tokens, { kind: 'text', text: ')' } as const]
        : tokens;
    };
    switch (current.kind) {
      case 'constant':
        return [
          current.valueType === 'Tag'
            ? { kind: 'source', text: current.text || '?' }
            : { kind: 'text', text: current.number ?? current.text ?? '?' },
        ];
      case 'parameter': {
        const parameter = parameters.get(current.name ?? '');
        return [
          {
            kind: 'parameter',
            text:
              parameter === undefined
                ? (current.name ?? '?')
                : `${current.name} ${parameterValueDisplay(parameter.defaultValue, parameter.displayUnit)}`,
          },
        ];
      }
      case 'value':
        return [{ kind: 'value', text: current.name ?? '?' }];
      case 'SMA':
        return [
          { kind: 'text', text: 'средняя' },
          ...input('in'),
          { kind: 'text', text: 'за' },
          ...input('period'),
        ];
      case 'ATR':
        return [{ kind: 'text', text: 'ATR за' }, ...input('period')];
      case 'EMA':
      case 'RSI':
        return [
          { kind: 'text', text: current.kind === 'EMA' ? 'экспоненциальная средняя' : 'RSI' },
          ...input('in'),
          { kind: 'text', text: 'за' },
          ...input('period'),
        ];
      case 'Bollinger':
        return [
          { kind: 'text', text: `${lower(fieldLabel(current.field ?? '?'))} Боллинджера` },
          ...input('in'),
          { kind: 'text', text: 'за' },
          ...input('period'),
          { kind: 'text', text: '±' },
          ...input('multiplier'),
          { kind: 'text', text: 'σ' },
        ];
      case 'MACD':
        return [
          { kind: 'text', text: lower(fieldLabel(current.field ?? '?')) },
          ...input('in'),
          { kind: 'text', text: 'за' },
          ...input('fastPeriod'),
          { kind: 'text', text: '/' },
          ...input('slowPeriod'),
          { kind: 'text', text: '/' },
          ...input('signalPeriod'),
        ];
      case 'previous':
        return [...nested('in'), { kind: 'text', text: 'на прошлой свече' }];
      case 'arithmetic': {
        const operation = current.operation ?? '?';
        if (operation === 'Min' || operation === 'Max') {
          return [
            { kind: 'text', text: operation === 'Min' ? 'меньшее из' : 'большее из' },
            ...input('a'),
            { kind: 'text', text: 'и' },
            ...input('b'),
          ];
        }
        return [
          ...nested('a'),
          { kind: 'text', text: operators[operation] ?? operation },
          ...nested('b'),
        ];
      }
      default: {
        const field =
          catalog?.valueSources
            .find((entry) => entry.key === current.kind)
            ?.fields?.find((entry) => entry.key === current.field)?.label ??
          fieldLabel(current.field ?? '?');
        const source: FormulaToken = {
          kind: 'source',
          text: `${sources[current.kind] ?? current.kind}: ${lower(field)}`,
        };
        if (current.kind === 'activeOrder') {
          const tag = current.arguments?.tag;
          return [
            source,
            ...(tag
              ? format(tag, depth + 1)
              : current.text
                ? [{ kind: 'source', text: current.text } as const]
                : []),
          ];
        }
        return [source];
      }
    }
  };
  return merge(format(expression, 0));
}

export function formulaText(tokens: readonly FormulaToken[]): string {
  return tokens
    .map((token) => token.text)
    .join(' ')
    .replaceAll('( ', '(')
    .replaceAll(' )', ')');
}

/** The expressions a block runs with now; settings saved for another order mode are left out. */
export function activeExpressions(block: StrategyBlock): (ValueExpression | null | undefined)[] {
  if (block.kind === 'order') {
    return [
      block.tag,
      orderSizeMode(block) === 'quantity' ? block.quantity : block.amount,
      showsOrderPrice(block) ? block.price : null,
    ];
  }
  if (block.kind === 'condition') {
    return (block.checks ?? []).flatMap((check) => checkExpressions(check));
  }
  return [block.tag];
}

function checkExpressions(check: StrategyCheck): (ValueExpression | null | undefined)[] {
  if (check.type !== 'OrderSizePreviewValid') {
    return [check.tag, ...Object.values(check.operands ?? {})];
  }
  const order = previewOrder(check);
  return [
    orderSizeMode(order) === 'quantity' ? check.operands?.quantity : check.operands?.amount,
    showsOrderPrice(order) ? check.operands?.price : null,
  ];
}

/** Named values a block depends on, each listed after the values it is built from. */
export function blockValues(block: StrategyBlock, definition: StrategyDefinition): string[] {
  const values = new Map(definition.values.map((value) => [value.name, value.expression]));
  const ordered: string[] = [];
  const visiting = new Set<string>();
  const visit = (name: string) => {
    if (ordered.includes(name) || visiting.has(name) || !values.has(name)) {
      return;
    }
    visiting.add(name);
    for (const nested of referencedValues(values.get(name)!)) {
      visit(nested);
    }
    visiting.delete(name);
    ordered.push(name);
  };
  for (const expression of activeExpressions(block)) {
    if (expression) {
      for (const name of referencedValues(expression)) {
        visit(name);
      }
    }
  }
  return ordered;
}

/** Other blocks that use a named value, directly or through other values. */
export function valueUsers(
  definition: StrategyDefinition,
  name: string,
  except: string,
): StrategyBlock[] {
  return definition.blocks.filter(
    (block) => block.id !== except && blockValues(block, definition).includes(name),
  );
}

export function blockInputs(definition: StrategyDefinition, id: string): StrategyWire[] {
  return definition.wires.filter((wire) => wire.to === id);
}

export function exitTargets(definition: StrategyDefinition, id: string, exit: string): string[] {
  return definition.wires
    .filter((wire) => wire.from.block === id && wire.from.exit === exit)
    .map((wire) => wire.to);
}

export type BlockTone = 'event' | 'decision' | 'buy' | 'sell' | 'cancel';
export function blockTone(block: StrategyBlock): BlockTone {
  return block.kind === 'event'
    ? 'event'
    : block.kind === 'condition'
      ? 'decision'
      : block.kind === 'cancel'
        ? 'cancel'
        : block.side === 'Sell'
          ? 'sell'
          : 'buy';
}

/** One check in words, with the values it compares kept as names. */
export function checkTokens(
  check: StrategyCheck,
  definition: StrategyDefinition,
  catalog?: BlockCatalog,
): FormulaToken[] {
  const input = (key: string) => formulaTokens(check.operands?.[key], definition, catalog);
  const text = (value: string): FormulaToken => ({ kind: 'text', text: value });
  switch (check.type) {
    case 'Compare':
      return merge([
        ...input('a'),
        text(operators[check.operation ?? ''] ?? check.operation ?? '?'),
        ...input('b'),
      ]);
    case 'CrossesAbove':
    case 'CrossesBelow':
      return merge([
        ...input('a'),
        text(check.type === 'CrossesAbove' ? 'пересекает снизу вверх' : 'пересекает сверху вниз'),
        ...input('b'),
      ]);
    case 'NoActiveTaggedOrder':
      return merge([
        text(check.operation === 'exists' ? 'Есть ордер' : 'Нет ордера'),
        ...formulaTokens(check.tag, definition, catalog),
      ]);
    case 'HasBaseBalance':
      return check.operands?.minQuantity
        ? merge([text('Есть актив, не меньше'), ...input('minQuantity')])
        : [text('Есть актив')];
    case 'PositionFlatPreview':
      return merge([
        text(check.operation === 'valid' ? 'Остаток можно продать:' : 'Остаток нельзя продать:'),
        ...input('quantity'),
      ]);
    case 'OrderSizePreviewValid': {
      const order = previewOrder(check);
      const quantity = orderSizeMode(order) === 'quantity';
      return merge([
        text(
          `Размер ордера допустим: ${check.side === 'Sell' ? 'продать' : 'купить'}${quantity ? '' : ' на'}`,
        ),
        ...input(quantity ? 'quantity' : 'amount'),
        ...(showsOrderPrice(order) ? [text('по'), ...input('price')] : []),
      ]);
    }
    default:
      return [
        text(
          catalog?.checkTypes.find((entry) => entry.key === check.type)?.label ||
            nodeTypeTitle(check.type),
        ),
      ];
  }
}
