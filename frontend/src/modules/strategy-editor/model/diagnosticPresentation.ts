import type { BlockCatalog, StrategyDefinition } from '@/modules/strategies';
import { strategyBlockLabel } from '@/modules/strategies';

export function authoringDiagnosticLabel(
  diagnostic: {
    blockId?: string | null;
    valueName?: string | null;
    parameterName?: string | null;
    checkIndex?: number | null;
  },
  definition: StrategyDefinition,
  catalog?: BlockCatalog,
): string {
  if (diagnostic.blockId) {
    const block = definition.blocks.find((item) => item.id === diagnostic.blockId);
    const label = block ? strategyBlockLabel(block, definition, catalog) : 'Блок';
    return diagnostic.checkIndex !== undefined && diagnostic.checkIndex !== null
      ? `${label}, проверка ${diagnostic.checkIndex + 1}`
      : label;
  }
  return diagnostic.valueName
    ? `Значение «${diagnostic.valueName}»`
    : diagnostic.parameterName
      ? `Параметр «${diagnostic.parameterName}»`
      : '';
}
