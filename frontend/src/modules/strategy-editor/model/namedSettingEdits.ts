import type { StrategyDefinition, ValueExpression } from '@/modules/strategies';

export function referencedValues(expression: ValueExpression): Set<string> {
  const found = new Set<string>();
  const pending = [expression];
  const seen = new Set<ValueExpression>();
  while (pending.length) {
    const item = pending.pop()!;
    if (seen.has(item)) {
      continue;
    }
    seen.add(item);
    if (item.kind === 'value' && item.name) {
      found.add(item.name);
    }
    pending.push(...Object.values(item.arguments ?? {}));
  }
  return found;
}

export function wouldCreateValueCycle(
  definition: StrategyDefinition,
  owner: string,
  expression: ValueExpression,
): boolean {
  const values = new Map(definition.values.map((value) => [value.name, value.expression]));
  const pending = [...referencedValues(expression)];
  const visited = new Set<string>();
  while (pending.length) {
    const name = pending.pop()!;
    if (name === owner) {
      return true;
    }
    if (visited.has(name)) {
      continue;
    }
    visited.add(name);
    const nested = values.get(name);
    if (nested) {
      pending.push(...referencedValues(nested));
    }
  }
  return false;
}

export function renameNamedSetting(
  definition: StrategyDefinition,
  kind: 'parameter' | 'value',
  oldName: string,
  nextName: string,
): StrategyDefinition {
  const name = nextName.trim();
  if (name === oldName) {
    return definition;
  }
  if (
    !name ||
    [...definition.parameters, ...definition.values].some((item) => item.name === name)
  ) {
    throw new Error('Введите уникальное имя параметра или значения.');
  }
  const settings = kind === 'parameter' ? definition.parameters : definition.values;
  if (!settings.some((item) => item.name === oldName)) {
    return definition;
  }
  // Only expression reference names change; tags, display names and parameter defaults remain exact.
  const rewritten = structuredClone(definition);
  const pending: unknown[] = [rewritten];
  const seen = new Set<object>();
  while (pending.length) {
    const item = pending.pop();
    if (item === null || typeof item !== 'object' || seen.has(item)) {
      continue;
    }
    seen.add(item);
    const record = item as Record<string, unknown>;
    if (record.kind === kind && record.name === oldName) {
      record.name = name;
    }
    pending.push(...Object.values(record));
  }
  return {
    ...rewritten,
    parameters: rewritten.parameters.map((item) =>
      kind === 'parameter' && item.name === oldName ? { ...item, name } : item,
    ),
    values: rewritten.values.map((item) =>
      kind === 'value' && item.name === oldName ? { ...item, name } : item,
    ),
  };
}
