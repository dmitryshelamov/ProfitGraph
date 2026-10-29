import type {
  StrategyBlock,
  StrategyCheck,
  StrategyDefinition,
  StrategyParameter,
  StrategyValue,
  StrategyWire,
} from '@/modules/strategies';

import type { Draft, EditorCommand } from './editorStore';
import { namedSettingDeletionMessage } from './namedSettingDependencies';
import { renameNamedSetting, wouldCreateValueCycle } from './namedSettingEdits';

export interface BlockExit {
  readonly nodeId: string;
  readonly port: string;
}
const edit = (
  label: string,
  apply: (definition: StrategyDefinition) => StrategyDefinition,
): EditorCommand => ({
  label,
  apply: (draft) => ({ ...draft, definition: apply(draft.definition) }),
});
export const wireId = (wire: StrategyWire) =>
  JSON.stringify([wire.from.block, wire.from.exit, wire.to]);
export function blockExits(block: StrategyBlock): readonly string[] {
  return block.kind === 'event'
    ? ['out']
    : block.kind === 'condition'
      ? ['yes', 'no']
      : block.kind === 'cancel'
        ? block.grid
          ? ['completed']
          : ['completed', 'rejected']
        : block.kind === 'order'
          ? ['submitted', 'filled', 'cancelled', 'rejected']
          : block.kind === 'grid'
            ? ['started', 'stopped']
            : [];
}
export function addBlock(block: StrategyBlock): EditorCommand {
  return edit('Добавление блока', (definition) => {
    if (definition.blocks.some((item) => item.id === block.id)) {
      throw new Error('Блок с таким ID уже существует');
    }
    return { ...definition, blocks: [...definition.blocks, block] };
  });
}
export function updateBlock(block: StrategyBlock, historyGroup?: string): EditorCommand {
  return {
    ...edit('Настройки блока', (definition) => ({
      ...definition,
      blocks: definition.blocks.map((item) => (item.id === block.id ? block : item)),
    })),
    historyGroup,
  };
}
export function deleteBlock(id: string): EditorCommand {
  return {
    label: 'Удаление блока',
    apply: (draft) => {
      const positions = { ...draft.layout.positions };
      delete positions[id];
      return {
        ...draft,
        layout: { positions },
        definition: {
          ...draft.definition,
          blocks: draft.definition.blocks.filter((block) => block.id !== id),
          wires: draft.definition.wires.filter((wire) => wire.from.block !== id && wire.to !== id),
        },
      };
    },
  };
}
export function connectBlocks(from: BlockExit, to: string): EditorCommand {
  return edit('Соединение блоков', (definition) => {
    const origin = definition.blocks.find((block) => block.id === from.nodeId);
    const target = definition.blocks.find((block) => block.id === to);
    if (!origin || !target || target.kind === 'event' || !blockExits(origin).includes(from.port)) {
      throw new Error('Выберите выход и вход существующих блоков');
    }
    const wire: StrategyWire = { from: { block: from.nodeId, exit: from.port }, to };
    if (definition.wires.some((item) => wireId(item) === wireId(wire))) {
      return definition;
    }
    // Lifecycle boundaries permit the order-event cycles supported by replay.
    const lifecycle = origin.kind === 'order' && ['filled', 'cancelled'].includes(from.port);
    if (!lifecycle) {
      const reached = new Set([to]);
      let changed = true;
      while (changed) {
        changed = false;
        for (const item of definition.wires) {
          const source = definition.blocks.find((block) => block.id === item.from.block);
          if (source?.kind === 'order' && ['filled', 'cancelled'].includes(item.from.exit)) {
            continue;
          }
          if (reached.has(item.from.block) && !reached.has(item.to)) {
            reached.add(item.to);
            changed = true;
          }
        }
      }
      if (reached.has(origin.id)) {
        throw new Error('Связь образует цикл без события исполнения или отмены ордера');
      }
    }
    return { ...definition, wires: [...definition.wires, wire] };
  });
}
export function disconnectWires(ids: readonly string[]): EditorCommand {
  const removed = new Set(ids);
  return edit('Удаление связей', (definition) => ({
    ...definition,
    wires: definition.wires.filter((wire) => !removed.has(wireId(wire))),
  }));
}
export function disconnectWire(id: string): EditorCommand {
  return disconnectWires([id]);
}
export function reconnectWire(id: string, from: BlockExit, to: string): EditorCommand {
  return {
    label: 'Перенос связи',
    apply: (draft) => connectBlocks(from, to).apply(disconnectWire(id).apply(draft)),
  };
}
export function insertBlock(from: BlockExit, block: StrategyBlock): EditorCommand {
  return {
    label: 'Добавление блока',
    apply: (draft) => connectBlocks(from, block.id).apply(addBlock(block).apply(draft)),
  };
}
const checks = (
  blockId: string,
  change: (checks: readonly StrategyCheck[]) => readonly StrategyCheck[],
) =>
  edit('Изменение проверок', (definition) => ({
    ...definition,
    blocks: definition.blocks.map((block) =>
      block.id === blockId && block.kind === 'condition'
        ? { ...block, checks: [...change(block.checks ?? [])] }
        : block,
    ),
  }));
export function addCheck(id: string, check: StrategyCheck): EditorCommand {
  return checks(id, (items) => [...items, check]);
}
export function updateCheck(
  id: string,
  index: number,
  check: StrategyCheck,
  historyGroup?: string,
): EditorCommand {
  return {
    ...checks(id, (items) => items.map((item, at) => (at === index ? check : item))),
    historyGroup,
  };
}
export function removeCheck(id: string, index: number): EditorCommand {
  return checks(id, (items) => items.filter((_, at) => at !== index));
}
export function reorderCheck(id: string, from: number, to: number): EditorCommand {
  return checks(id, (items) => {
    if (from < 0 || to < 0 || from >= items.length || to >= items.length) {
      return items;
    }
    const result = [...items];
    const [item] = result.splice(from, 1);
    result.splice(to, 0, item!);
    return result;
  });
}
export function setConditionJoin(id: string, join: 'all' | 'any'): EditorCommand {
  return edit('Все / любое', (definition) => ({
    ...definition,
    blocks: definition.blocks.map((block) =>
      block.id === id && block.kind === 'condition' ? { ...block, join } : block,
    ),
  }));
}
export function upsertParameter(
  parameter: StrategyParameter,
  historyGroup?: string,
): EditorCommand {
  return {
    ...edit('Параметр', (definition) => ({
      ...definition,
      parameters: definition.parameters.some((item) => item.name === parameter.name)
        ? definition.parameters.map((item) => (item.name === parameter.name ? parameter : item))
        : [...definition.parameters, parameter],
    })),
    historyGroup,
  };
}
export function removeParameter(name: string): EditorCommand {
  return edit('Удаление параметра', (definition) => {
    if (!definition.parameters.some((parameter) => parameter.name === name)) {
      return definition;
    }
    const message = namedSettingDeletionMessage(definition, 'parameter', name);
    if (message) {
      throw new Error(message);
    }
    return {
      ...definition,
      parameters: definition.parameters.filter((item) => item.name !== name),
    };
  });
}
export function upsertValue(value: StrategyValue, historyGroup?: string): EditorCommand {
  return {
    ...edit('Значение', (definition) => {
      if (wouldCreateValueCycle(definition, value.name, value.expression)) {
        throw new Error(`Значение «${value.name}» образует цикл ссылок. Выберите другое значение.`);
      }
      return {
        ...definition,
        values: definition.values.some((item) => item.name === value.name)
          ? definition.values.map((item) => (item.name === value.name ? value : item))
          : [...definition.values, value],
      };
    }),
    historyGroup,
  };
}
export function removeValue(name: string): EditorCommand {
  return edit('Удаление значения', (definition) => {
    if (!definition.values.some((value) => value.name === name)) {
      return definition;
    }
    const message = namedSettingDeletionMessage(definition, 'value', name);
    if (message) {
      throw new Error(message);
    }
    return { ...definition, values: definition.values.filter((item) => item.name !== name) };
  });
}
export function renameStrategy(name: string): EditorCommand {
  return {
    label: 'Название стратегии',
    historyGroup: 'rename',
    apply: (draft) => ({ ...draft, name }),
  };
}
export function moveBlocks(positions: Draft['layout']['positions']): EditorCommand {
  return {
    label: 'Перемещение блока',
    apply: (draft) => ({
      ...draft,
      layout: { positions: { ...draft.layout.positions, ...positions } },
    }),
  };
}
export function resetFlowLayout(): EditorCommand {
  return { label: 'Выстроить', apply: (draft) => ({ ...draft, layout: { positions: {} } }) };
}
export function keepFlowPlaces(
  command: EditorCommand,
  current: Draft['layout']['positions'],
  placed: Draft['layout']['positions'] = {},
): EditorCommand {
  return {
    ...command,
    apply: (draft) => {
      const next = command.apply(draft);
      const ids = new Set(next.definition.blocks.map((block) => block.id));
      return {
        ...next,
        layout: {
          positions: Object.fromEntries(
            Object.entries({ ...current, ...next.layout.positions, ...placed }).filter(([id]) =>
              ids.has(id),
            ),
          ),
        },
      };
    },
  };
}

export function renameParameter(oldName: string, name: string): EditorCommand {
  return edit('Переименование параметра', (definition) =>
    renameNamedSetting(definition, 'parameter', oldName, name),
  );
}
export function renameValue(oldName: string, name: string): EditorCommand {
  return edit('Переименование значения', (definition) =>
    renameNamedSetting(definition, 'value', oldName, name),
  );
}
