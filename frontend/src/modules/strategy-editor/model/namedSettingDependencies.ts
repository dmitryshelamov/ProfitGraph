import type { StrategyDefinition, ValueExpression } from '@/modules/strategies';
import { strategyBlockLabel } from '@/modules/strategies';

export type NamedSettingKind = 'parameter' | 'value';

interface References {
  readonly parameters: ReadonlySet<string>;
  readonly values: ReadonlySet<string>;
}

function references(expressions: readonly (ValueExpression | null | undefined)[]): References {
  const parameters = new Set<string>();
  const values = new Set<string>();
  const seen = new Set<ValueExpression>();
  const pending = [...expressions];
  while (pending.length > 0) {
    const expression = pending.pop();
    if (!expression || seen.has(expression)) {
      continue;
    }
    seen.add(expression);
    if (expression.kind === 'parameter' && expression.name) {
      parameters.add(expression.name);
    }
    if (expression.kind === 'value' && expression.name) {
      values.add(expression.name);
    }
    pending.push(...Object.values(expression.arguments ?? {}));
  }
  return { parameters, values };
}

/** Scans each expression tree once; affected shared values are followed through names with cycle protection. */
export function createNamedSettingDeletionLookup(
  definition: StrategyDefinition,
): (kind: NamedSettingKind, name: string) => string | null {
  const parameterValues = new Map<string, Set<string>>();
  const valueValues = new Map<string, Set<string>>();
  const parameterBlocks = new Map<string, Set<string>>();
  const valueBlocks = new Map<string, Set<string>>();
  const cache = new Map<string, string | null>();
  const add = (index: Map<string, Set<string>>, names: ReadonlySet<string>, owner: string) => {
    for (const name of names) {
      const owners = index.get(name) ?? new Set<string>();
      owners.add(owner);
      index.set(name, owners);
    }
  };
  for (const value of definition.values) {
    const refs = references([value.expression]);
    add(parameterValues, refs.parameters, value.name);
    add(valueValues, refs.values, value.name);
  }
  for (const block of definition.blocks) {
    const expressions = [block.tag, block.amount, block.quantity, block.price];
    for (const check of block.checks ?? []) {
      expressions.push(check.tag, ...Object.values(check.operands ?? {}));
    }
    const refs = references(expressions);
    add(parameterBlocks, refs.parameters, block.id);
    add(valueBlocks, refs.values, block.id);
  }
  return (kind, name) => {
    const key = JSON.stringify([kind, name]);
    if (cache.has(key)) {
      return cache.get(key)!;
    }
    const affectedValues = new Set<string>();
    const affectedBlocks = new Set(
      (kind === 'parameter' ? parameterBlocks : valueBlocks).get(name),
    );
    const pending = [...((kind === 'parameter' ? parameterValues : valueValues).get(name) ?? [])];
    while (pending.length > 0) {
      const value = pending.pop()!;
      // A self-reference disappears with the deleted value. Other members of a cycle still block deletion.
      if ((kind === 'value' && value === name) || affectedValues.has(value)) {
        continue;
      }
      affectedValues.add(value);
      for (const block of valueBlocks.get(value) ?? []) {
        affectedBlocks.add(block);
      }
      pending.push(...(valueValues.get(value) ?? []));
    }
    const usages = definition.values
      .filter((value) => affectedValues.has(value.name))
      .map((value) => `значении «${value.name}»`);
    for (const block of definition.blocks) {
      if (affectedBlocks.has(block.id)) {
        const label = strategyBlockLabel(block, definition);
        usages.push(`блоке «${label}»`);
      }
    }
    const message =
      usages.length === 0
        ? null
        : `Нельзя удалить ${kind === 'parameter' ? 'параметр' : 'значение'} «${name}»: используется в ${usages.join(', ')}.`;
    cache.set(key, message);
    return message;
  };
}

export function namedSettingDeletionMessage(
  definition: StrategyDefinition,
  kind: NamedSettingKind,
  name: string,
): string | null {
  return createNamedSettingDeletionLookup(definition)(kind, name);
}
