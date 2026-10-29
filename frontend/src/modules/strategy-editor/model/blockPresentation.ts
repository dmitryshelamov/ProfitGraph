import type {
  BlockCatalog,
  StrategyBlock,
  StrategyCheck,
  StrategyDefinition,
  ValueExpression,
} from '@/modules/strategies';
import { parameterValueDisplay } from '@/modules/strategies';
import { nodeTypeTitle, portLabel, strategyBlockLabel } from '@/modules/strategies';

import { blockExits, wireId } from './editorCommands';
import { needsOrderConversion, orderSizeMode, showsOrderPrice } from './orderSizeSettings';

export type FlowchartKind = 'start' | 'decision' | 'action';
export interface FlowchartNode {
  readonly id: string;
  readonly kind: FlowchartKind;
  readonly typeKey: string;
  readonly title: string;
  readonly side?: string | null;
  readonly lines: readonly string[];
  readonly mode?: string;
  readonly details: readonly (readonly [string, string])[];
  readonly outputs: readonly string[];
  readonly detached: boolean;
}
export interface FlowchartEdge {
  readonly id: string;
  readonly source: string;
  readonly sourcePort: string;
  readonly target: string;
  readonly targetPort: string;
}
export interface FlowchartBoard {
  readonly nodes: readonly FlowchartNode[];
  readonly edges: readonly FlowchartEdge[];
}
const eventNames: Record<string, string> = {
  yes: 'да',
  no: 'нет',
  filled: 'исполнен',
  submitted: 'выставлен',
  rejected: 'отклонён',
  cancelled: 'отменён',
  completed: 'отменён',
};
const operators: Record<string, string> = {
  Add: '+',
  Subtract: '−',
  Multiply: '×',
  Divide: '÷',
  Modulo: 'mod',
  '<=': '≤',
  '>=': '≥',
  '!=': '≠',
};
export function eventExitLabel(port: string): string {
  return eventNames[port] ?? (port === 'out' ? '' : portLabel(port));
}
export function expressionText(
  expression: ValueExpression | null | undefined,
  definition: StrategyDefinition,
  seen = new Set<string>(),
): string {
  const values = new Map(definition.values.map((value) => [value.name, value.expression]));
  const parameters = new Map(
    definition.parameters.map((parameter) => [parameter.name, parameter.defaultValue]),
  );
  const cache = new Map<string, string>();
  const visiting = new Set(seen);
  let remaining = 512;
  const compact = (text: string) => (text.length > 160 ? `${text.slice(0, 159)}…` : text);
  const format = (current: ValueExpression | null | undefined, depth: number): string => {
    if (!current) {
      return '?';
    }
    if (remaining-- <= 0 || depth > 128) {
      return current.name ?? '…';
    }
    const input = (key: string) => format(current.arguments?.[key], depth + 1);
    if (current.kind === 'constant') {
      return compact(current.number ?? current.text ?? '?');
    }
    if (current.kind === 'parameter') {
      const name = current.name ?? '';
      const value = parameters.get(name);
      return compact(
        value === undefined
          ? (current.name ?? '?')
          : parameterValueDisplay(
              value,
              definition.parameters.find((parameter) => parameter.name === name)?.displayUnit,
            ),
      );
    }
    if (current.kind === 'value') {
      const name = current.name;
      if (!name || visiting.has(name)) {
        return name ?? '?';
      }
      const cached = cache.get(name);
      if (cached !== undefined) {
        return cached;
      }
      visiting.add(name);
      const text = format(values.get(name), depth + 1);
      visiting.delete(name);
      cache.set(name, text);
      return text;
    }
    if (['SMA', 'ATR', 'EMA', 'RSI'].includes(current.kind)) {
      return compact(`${current.kind} ${input('period')}`);
    }
    if (current.kind === 'Bollinger') {
      return compact(
        `BB ${input('period')}×${input('multiplier')}: ${portLabel(current.field ?? '?')}`,
      );
    }
    if (current.kind === 'MACD') {
      return compact(
        `MACD ${input('fastPeriod')}/${input('slowPeriod')}/${input('signalPeriod')}: ${portLabel(current.field ?? '?')}`,
      );
    }
    if (current.kind === 'previous') {
      return compact(`Предыдущее: ${input('in')}`);
    }
    if (current.kind === 'arithmetic') {
      const op = current.operation ?? '?';
      return compact(
        ['Min', 'Max'].includes(op)
          ? `${op === 'Min' ? 'меньшее' : 'большее'} из ${input('a')} и ${input('b')}`
          : `${input('a')} ${operators[op] ?? op} ${input('b')}`,
      );
    }
    const labels: Record<string, string> = {
      candle: 'Свеча',
      balance: 'Баланс',
      position: 'Позиция',
      activeOrder: 'Активный ордер',
    };
    return compact(`${labels[current.kind] ?? current.kind}: ${portLabel(current.field ?? '?')}`);
  };
  return format(expression, 0);
}
export function checkText(
  check: StrategyCheck,
  definition: StrategyDefinition,
  catalog?: BlockCatalog,
): string {
  const input = (key: string) => expressionText(check.operands?.[key], definition);
  if (check.type === 'Compare') {
    return `${input('a')} ${operators[check.operation ?? ''] ?? check.operation ?? '?'} ${input('b')}`;
  }
  if (check.type === 'CrossesAbove' || check.type === 'CrossesBelow') {
    return `${input('a')} ${check.type === 'CrossesAbove' ? '↗' : '↘'} ${input('b')}`;
  }
  if (check.type === 'NoActiveTaggedOrder') {
    return `${check.operation === 'exists' ? 'Есть' : 'Нет'} ордера «${expressionText(check.tag, definition)}»`;
  }
  if (check.type === 'PositionFlatPreview' && check.operation === 'valid') {
    return 'Продажа возможна';
  }
  return (
    catalog?.checkTypes.find((entry) => entry.key === check.type)?.label ||
    nodeTypeTitle(check.type)
  );
}
function present(
  block: StrategyBlock,
  definition: StrategyDefinition,
  reached: ReadonlySet<string>,
  catalog?: BlockCatalog,
): FlowchartNode {
  const kind =
    block.kind === 'event' ? 'start' : block.kind === 'condition' ? 'decision' : 'action';
  const details: (readonly [string, string])[] = [];
  const lines =
    block.kind === 'condition'
      ? block.checks?.length
        ? block.checks.map((check) => checkText(check, definition, catalog))
        : ['Выберите условие']
      : [];
  let mode: string | undefined;
  if (block.kind === 'condition') {
    mode =
      block.join === 'any'
        ? 'любое условие'
        : (block.checks?.length ?? 0) > 1
          ? 'все условия'
          : undefined;
    details.push(...lines.map((line) => ['', line] as const));
  }
  if (block.kind === 'order') {
    mode = `${block.side === 'Sell' ? 'Продать' : 'Купить'} · ${block.orderKind === 'Limit' ? 'лимит' : 'по рынку'}`;
    const quantityMode = orderSizeMode(block) === 'quantity';
    const size = quantityMode
      ? block.quantity?.kind === 'balance' && block.quantity.field === 'baseAvailable'
        ? 'весь доступный актив'
        : expressionText(block.quantity, definition)
      : expressionText(block.amount, definition);
    details.push([
      quantityMode ? 'Кол-во' : 'Сумма',
      needsOrderConversion(block) ? `≈ ${size} по цене сигнала` : size,
    ]);
    if (showsOrderPrice(block)) {
      details.push([
        block.orderKind === 'Limit' ? 'Цена' : 'Цена пересчёта',
        expressionText(block.price, definition),
      ]);
    }
  }
  if (block.kind === 'order' || block.kind === 'cancel') {
    details.push(['Метка', expressionText(block.tag, definition)]);
  }
  const title = strategyBlockLabel(block, definition, catalog);
  return {
    id: block.id,
    kind,
    typeKey: block.kind,
    side: block.side,
    title,
    lines,
    mode,
    details,
    outputs: blockExits(block),
    detached: !reached.has(block.id),
  };
}
/** Formats the stored blocks for canvas rendering; never creates execution nodes or edits the definition. */
export function presentBlocks(
  definition: StrategyDefinition,
  catalog?: BlockCatalog,
): FlowchartBoard {
  const reached = new Set(
    definition.blocks.filter((block) => block.kind === 'event').map((block) => block.id),
  );
  let changed = true;
  while (changed) {
    changed = false;
    for (const wire of definition.wires) {
      if (reached.has(wire.from.block) && !reached.has(wire.to)) {
        reached.add(wire.to);
        changed = true;
      }
    }
  }
  return {
    nodes: definition.blocks.map((block) => present(block, definition, reached, catalog)),
    edges: definition.wires.map((wire) => ({
      id: wireId(wire),
      source: wire.from.block,
      sourcePort: wire.from.exit,
      target: wire.to,
      targetPort:
        definition.blocks.find((block) => block.id === wire.to)?.kind === 'condition'
          ? 'event'
          : 'trigger',
    })),
  };
}
