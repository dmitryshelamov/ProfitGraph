import { Handle, type Node, type NodeProps, Position } from '@xyflow/react';
import type { KeyboardEvent } from 'react';

import type { FlowchartNode as PresentedNode } from '../model/blockPresentation';
import { eventExitLabel } from '../model/blockPresentation';
import { inputPort, nodeSize } from '../model/flowchartLayout';

export type FlowchartNodeData = Record<string, unknown> & {
  readonly presentation: PresentedNode;
  readonly selected: boolean;
  readonly diagnostics: readonly string[];
  readonly connected: readonly string[];
  readonly onSelect: () => void;
  readonly onOpen: (port: string) => void;
  readonly onDelete: () => void;
};

export type FlowchartTone = 'event' | 'decision' | 'buy' | 'sell' | 'cancel' | 'step';

export function flowchartTone(node: PresentedNode): FlowchartTone {
  if (node.kind === 'start') {
    return 'event';
  }
  if (node.kind === 'decision') {
    return 'decision';
  }
  if (node.typeKey === 'order' && node.side !== 'Sell') {
    return 'buy';
  }
  if (node.typeKey === 'order' && node.side === 'Sell') {
    return 'sell';
  }
  return node.typeKey === 'cancel' ? 'cancel' : 'step';
}

const kindTitles: Record<FlowchartTone, string> = {
  event: 'Событие',
  decision: 'Условие',
  buy: 'Ордер',
  sell: 'Ордер',
  cancel: 'Отмена',
  step: 'Действие',
};

/** The block's own name, when it says more than its kind. */
function blockName(node: PresentedNode): string | null {
  if (node.kind === 'decision') {
    return node.title === 'Если' ? null : node.title;
  }
  if (node.typeKey === 'cancel') {
    return node.title === 'Отмена ордера' ? null : node.title;
  }
  return node.title;
}

export function FlowchartNode({ data }: NodeProps<Node<FlowchartNodeData>>) {
  const item = data.presentation;
  const tone = flowchartTone(item);
  const input = inputPort(item);
  const name = blockName(item);
  const size = nodeSize(item);
  const label =
    item.kind === 'decision'
      ? `${name ? `${name}:` : 'Если'} ${item.lines.join(' ')}`
      : item.kind === 'start'
        ? `Когда: ${item.title}`
        : [item.title, ...item.lines].join(' — ');
  return (
    <div
      data-flowchart-node={item.id}
      title={data.diagnostics.join('\n') || undefined}
      style={{ width: size.width, height: size.height }}
      className={`flowchart-node flowchart-${item.kind} tone-${tone} ${data.selected ? 'is-selected' : ''} ${data.diagnostics.length ? 'has-diagnostic' : ''} ${item.detached ? 'is-detached' : ''}`}
    >
      <div className="flowchart-head" title="Потяните, чтобы передвинуть блок">
        <i aria-hidden="true" />
        <span>
          {kindTitles[tone]}
          {item.detached && <em> · не подключён</em>}
        </span>
        {name && <b>{name}</b>}
        <button
          type="button"
          className="flowchart-node-delete nodrag"
          aria-label={`Удалить блок ${item.id}`}
          title="Удалить блок"
          onClick={data.onDelete}
        >
          ×
        </button>
      </div>
      <div className="flowchart-ports">
        <div>
          {input && (
            <div className="flowchart-port is-input">
              <Handle id={input} type="target" position={Position.Left} />
              когда
            </div>
          )}
        </div>
        <div>
          {item.outputs.map((key) => {
            const open = !data.connected.includes(key);
            const exit = eventExitLabel(key) || 'событие';
            return (
              <div
                key={key}
                data-exit={key}
                className={`flowchart-port is-output ${open ? 'is-open' : ''}`}
              >
                {exit}
                {/* Every exit is itself a "+": a click picks the next block, a drag draws a wire. */}
                <Handle
                  id={key}
                  type="source"
                  position={Position.Right}
                  role="button"
                  tabIndex={0}
                  title={`Добавить блок после «${exit}» или протянуть провод`}
                  aria-label={`Добавить после ${item.id} ${eventExitLabel(key) || 'событие'}`}
                  onClick={() => data.onOpen(key)}
                  onKeyDown={(event: KeyboardEvent<HTMLDivElement>) => {
                    if (event.key === 'Enter' || event.key === ' ') {
                      event.preventDefault();
                      data.onOpen(key);
                    }
                  }}
                >
                  <svg viewBox="0 0 8 8" aria-hidden="true">
                    <path d="M4 1.2v5.6M1.2 4h5.6" />
                  </svg>
                </Handle>
              </div>
            );
          })}
        </div>
      </div>
      {(item.mode || item.details.length > 0) && (
        <button
          type="button"
          className="flowchart-details nodrag"
          aria-label={label}
          onClick={data.onSelect}
        >
          {item.mode && <span className="flowchart-mode">{item.mode}</span>}
          {item.details.map(([key, value], index) => (
            <span key={index} title={value}>
              <small>{key ? `${key}:` : '·'}</small> <em>{value}</em>
            </span>
          ))}
        </button>
      )}
      {data.diagnostics.length > 0 && (
        <span role="alert" className="flowchart-diagnostic">
          {data.diagnostics[0]}
          {data.diagnostics.length > 1 && ` (+${data.diagnostics.length - 1})`}
        </span>
      )}
    </div>
  );
}
