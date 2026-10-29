import '@xyflow/react/dist/style.css';
import './board.css';
import './strategyEditor.css';

import {
  Background,
  BackgroundVariant,
  type Edge,
  type Node,
  type NodeChange,
  Panel,
  ReactFlow,
  type ReactFlowInstance,
  useReactFlow,
  useViewport,
} from '@xyflow/react';
import { Fragment, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';

import {
  type BlockCatalog,
  type GraphDiagnostic,
  strategyBlockLabel,
  type StrategyDefinition,
} from '@/modules/strategies';

import { isValidConnection } from '../connections/connectionRules';
import { eventExitLabel, presentBlocks } from '../model/blockPresentation';
import { createBlock } from '../model/catalogBlock';
import {
  addBlock,
  connectBlocks,
  deleteBlock,
  disconnectWire,
  disconnectWires,
  insertBlock,
  keepFlowPlaces,
  moveBlocks,
  reconnectWire,
  resetFlowLayout,
} from '../model/editorCommands';
import { type EditorCommand, useEditorStore } from '../model/editorStore';
import {
  freeSpot,
  layoutFlowchart,
  type LayoutPoint,
  NODE_WIDTH,
  nodeSize,
} from '../model/flowchartLayout';
import { routeFlowchart } from '../model/flowchartRoutes';
import { FlowchartNode, type FlowchartNodeData } from '../nodes/FlowchartNode';
import { FlowchartWire, type FlowchartWireData } from '../nodes/FlowchartWire';
import { wireLook } from '../nodes/wireLook';

const nodeTypes = { flowchart: FlowchartNode };
const blockMime = 'application/x-profit-fox-block';
const PICKER_WIDTH = 220;
const PICKER_HEIGHT = 290;
/** Keeps a menu opened at a screen point inside the board. */
function menuPlace(box: DOMRect, x: number, y: number) {
  return {
    left: Math.min(Math.max(x - box.left, 8), box.width - PICKER_WIDTH - 8),
    top: Math.min(Math.max(y - box.top, 8), box.height - PICKER_HEIGHT - 8),
  };
}
const edgeTypes = { wire: FlowchartWire };
// The fitted chart keeps clear of the floating bars around the edges of the board.
const fitOptions = {
  padding: { top: '72px', right: '24px', bottom: '72px', left: '76px' },
  minZoom: 0.4,
  maxZoom: 1,
} as const;

/** Zoom bar in the bottom right corner, as on a Miro board. */
function ZoomBar() {
  const { zoomIn, zoomOut, fitView, zoomTo } = useReactFlow();
  const { zoom } = useViewport();
  return (
    <div className="board-bar" role="toolbar" aria-label="Управление холстом">
      <button
        type="button"
        className="board-button board-icon"
        aria-label="Показать весь граф"
        title="Показать весь граф"
        onClick={() => void fitView(fitOptions)}
      >
        <svg viewBox="0 0 16 16" aria-hidden="true">
          <path d="M2 6V2h4M10 2h4v4M14 10v4h-4M6 14H2v-4" />
        </svg>
      </button>
      <button
        type="button"
        className="board-button board-icon"
        aria-label="Уменьшить масштаб"
        title="Уменьшить масштаб"
        onClick={() => void zoomOut()}
      >
        −
      </button>
      <button
        type="button"
        className="board-button board-zoom"
        title="Масштаб 100%"
        onClick={() => void zoomTo(1)}
      >
        {Math.round(zoom * 100)}%
      </button>
      <button
        type="button"
        className="board-button board-icon"
        aria-label="Увеличить масштаб"
        title="Увеличить масштаб"
        onClick={() => void zoomIn()}
      >
        +
      </button>
    </div>
  );
}

export function FlowchartEditor({
  definition,
  catalog,
  diagnostics,
  focusRequest,
}: {
  definition: StrategyDefinition;
  catalog: BlockCatalog;
  diagnostics: readonly GraphDiagnostic[];
  focusRequest?: { nodeId: string; sequence: number };
}) {
  const editor = useEditorStore();
  const projection = useMemo(() => presentBlocks(definition, catalog), [definition, catalog]);
  const layout = useMemo(() => layoutFlowchart(projection), [projection]);
  const saved = editor.draft?.layout.positions;
  const [dragging, setDragging] = useState<Record<string, LayoutPoint>>({});
  const [instance, setInstance] =
    useState<ReactFlowInstance<Node<FlowchartNodeData>, Edge<FlowchartWireData>>>();
  const picker = editor.picker;
  const setPicker = editor.openPicker;
  const [error, setError] = useState<string>();
  const [menu, setMenu] = useState<'add' | 'help'>();
  // Where a wire dropped in empty space should place the block picked for it.
  const [drop, setDrop] = useState<LayoutPoint>();
  // The picker opens next to the point where the wire was let go.
  const [pickerAt, setPickerAt] = useState<{ left: number; top: number }>();
  const canvas = useRef<HTMLDivElement>(null);
  const placedByDrop = useRef(false);
  // The picker opens next to the exit it continues, like a context menu.
  useLayoutEffect(() => {
    if (!picker) {
      return;
    }
    if (placedByDrop.current) {
      placedByDrop.current = false;
      return;
    }
    const box = canvas.current?.getBoundingClientRect();
    const port = canvas.current?.querySelector(
      `[data-flowchart-node="${CSS.escape(picker.nodeId)}"] .react-flow__handle[data-handleid="${CSS.escape(picker.port)}"]`,
    );
    if (!box || !port) {
      setPickerAt(undefined);
      return;
    }
    const at = port.getBoundingClientRect();
    setPickerAt(menuPlace(box, at.right + 8, at.top + at.height / 2 - 18));
  }, [picker?.nodeId, picker?.port]);
  const [hoveredEdge, setHoveredEdge] = useState<string>();
  const reconnected = useRef(false);
  const execute = (make: () => void) => {
    try {
      make();
      setError(undefined);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Не удалось изменить блок-схему');
    }
  };
  // A block the user moved keeps its place; every other block follows the grid layout.
  const positions = useMemo(() => {
    const result: Record<string, LayoutPoint> = {};
    for (const node of projection.nodes) {
      result[node.id] = dragging[node.id] ??
        saved?.[node.id] ??
        layout.nodes[node.id] ?? { x: 0, y: 0 };
    }
    return result;
  }, [projection, layout, saved, dragging]);
  // Wires go around the blocks and never merge into one line.
  const routes = useMemo(
    () => routeFlowchart(projection.nodes, projection.edges, positions),
    [projection, positions],
  );
  const change = (command: EditorCommand, placed?: Record<string, LayoutPoint>) =>
    execute(() => editor.execute(keepFlowPlaces(command, positions, placed)));
  useEffect(() => {
    if (!focusRequest || !instance) {
      return;
    }
    const node = projection.nodes.find((item) => item.id === focusRequest.nodeId);
    const position = positions[focusRequest.nodeId];
    if (node && position) {
      const size = nodeSize(node);
      void instance.setCenter(position.x + size.width / 2, position.y + size.height / 2, {
        zoom: instance.getZoom(),
        duration: 300,
      });
    }
    // Only a new focus request moves the view, not every change of the layout.
  }, [focusRequest, instance]);
  const create = (type: string) =>
    createBlock(type, `${type.toLowerCase()}_${crypto.randomUUID().slice(0, 8)}`);
  const insert = (type: string) => {
    if (!picker) {
      return;
    }
    const node = create(type);
    if (!node) {
      return;
    }
    execute(() => {
      const source = positions[picker.nodeId]!;
      // A new block appears where its wire was dropped, or right of the exit it continues.
      const place = freeSpot(
        drop ?? { x: source.x + NODE_WIDTH + 72, y: source.y },
        { width: NODE_WIDTH, height: 140 },
        projection.nodes.map((item) => ({ ...positions[item.id]!, ...nodeSize(item) })),
      );
      editor.execute(keepFlowPlaces(insertBlock(picker, node), positions, { [node.id]: place }));
      editor.select(node.id);
      setPicker(null);
      setDrop(undefined);
      setPickerAt(undefined);
    });
  };
  const closePicker = () => {
    setPicker(null);
    setDrop(undefined);
    setPickerAt(undefined);
  };
  // A free block appears where it was dropped, or in the middle of the visible board.
  const addFree = (type: string, at?: { x: number; y: number }) => {
    setMenu(undefined);
    const node = create(type);
    if (!node || !instance) {
      return;
    }
    const box = canvas.current?.getBoundingClientRect();
    const point = instance.screenToFlowPosition(
      at ?? {
        x: (box?.left ?? 0) + (box?.width ?? 0) / 2,
        y: (box?.top ?? 0) + (box?.height ?? 0) / 2,
      },
    );
    const place = freeSpot(
      { x: point.x - NODE_WIDTH / 2, y: point.y - 20 },
      { width: NODE_WIDTH, height: 140 },
      projection.nodes.map((item) => ({ ...positions[item.id]!, ...nodeSize(item) })),
    );
    change(addBlock(node), { [node.id]: place });
    editor.select(node.id);
  };
  // Delete removes the selected block unless the user is typing.
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      if (event.key === 'Escape') {
        setMenu(undefined);
        closePicker();
        return;
      }
      if (
        (event.key !== 'Delete' && event.key !== 'Backspace') ||
        !editor.selectedNodeId ||
        target?.closest('input, textarea, select, [contenteditable="true"]')
      ) {
        return;
      }
      event.preventDefault();
      change(deleteBlock(editor.selectedNodeId));
      editor.select(null);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  });
  const projectedEdge = (id: string) => projection.edges.find((edge) => edge.id === id);
  const flowNodes: Node<FlowchartNodeData>[] = projection.nodes.map((item) => ({
    id: item.id,
    type: 'flowchart',
    position: positions[item.id]!,
    // The block's size is known up front; without it React Flow hides and re-measures every block on each frame.
    measured: nodeSize(item),
    dragHandle: '.flowchart-head',
    selectable: false,
    data: {
      presentation: item,
      selected: editor.selectedNodeId === item.id,
      diagnostics: diagnostics
        .filter((diagnostic) => diagnostic.blockId === item.id)
        .map((diagnostic) => diagnostic.message),
      connected: projection.edges
        .filter((edge) => edge.source === item.id)
        .map((edge) => edge.sourcePort),
      onSelect: () => editor.select(item.id),
      onOpen: (port) => setPicker({ nodeId: item.id, port }),
      onDelete: () => {
        change(deleteBlock(item.id));
        if (editor.selectedNodeId === item.id) {
          editor.select(null);
        }
      },
    },
  }));
  const flowEdges: Edge<FlowchartWireData>[] = projection.edges.map((edge) => {
    const hovered = hoveredEdge === edge.id;
    const highlighted =
      hovered || editor.selectedNodeId === edge.source || editor.selectedNodeId === edge.target;
    const source = projection.nodes.find((node) => node.id === edge.source);
    const look = source ? wireLook(source, edge.sourcePort) : undefined;
    const color = look?.color ?? '#7f8aa0';
    return {
      id: edge.id,
      type: 'wire',
      source: edge.source,
      sourceHandle: edge.sourcePort,
      target: edge.target,
      targetHandle: edge.targetPort,
      reconnectable: true,
      interactionWidth: 16,
      data: { points: routes[edge.id], label: look?.label, showLabel: hovered, color },
      style: {
        stroke: color,
        strokeWidth: highlighted ? 2.8 : 1.8,
        strokeDasharray: look?.dashed ? '7 5' : undefined,
        filter: highlighted ? `drop-shadow(0 0 3px ${color}aa)` : undefined,
      },
    };
  });
  const onNodesChange = (changes: NodeChange<Node<FlowchartNodeData>>[]) => {
    const moved: Record<string, LayoutPoint> = {};
    for (const change of changes) {
      if (change.type === 'position' && change.position) {
        moved[change.id] = change.position;
      }
    }
    if (Object.keys(moved).length) {
      setDragging((current) => ({ ...current, ...moved }));
    }
  };
  const actionDefinitions = catalog.orderKinds.flatMap((order) =>
    ['Buy', 'Sell'].map((side) => ({
      key: `${order.key}${side}`,
      label: strategyBlockLabel({ id: '', kind: 'order', orderKind: order.key, side }),
    })),
  );
  actionDefinitions.push(
    { key: 'grid', label: 'Сетка' },
    { key: 'cancel', label: 'Отмена ордера' },
  );
  const palette = [
    {
      title: 'Событие',
      items: catalog.eventSources.map((source) => ({ key: source.key, label: source.label })),
    },
    { title: 'Условие', items: [{ key: 'condition', label: 'Если' }] },
    { title: 'Действие', items: actionDefinitions },
  ];
  return (
    <section aria-label="Блок-схема стратегии" className="flowchart-board">
      <div className="board-bar board-tools" role="toolbar" aria-label="Инструменты">
        <button
          type="button"
          className="board-button board-tool"
          aria-label="Добавить блок"
          title="Добавить блок: нажмите или перетащите на холст"
          aria-expanded={menu === 'add'}
          onClick={() => setMenu((current) => (current === 'add' ? undefined : 'add'))}
        >
          <svg viewBox="0 0 16 16" aria-hidden="true">
            <path d="M8 2.5v11M2.5 8h11" />
          </svg>
        </button>
        <button
          type="button"
          className="board-button board-tool"
          aria-label="Выстроить"
          title="Выстроить блоки заново"
          onClick={() => {
            setMenu(undefined);
            execute(() => editor.execute(resetFlowLayout()));
            requestAnimationFrame(() => void instance?.fitView(fitOptions));
          }}
        >
          <svg viewBox="0 0 16 16" aria-hidden="true">
            <path d="M2 2h5v5H2zM9 2h5v5H9zM2 9h5v5H2zM9 9h5v5H9z" />
          </svg>
        </button>
        <button
          type="button"
          className="board-button board-tool"
          aria-label="Подсказка"
          title="Подсказка"
          aria-expanded={menu === 'help'}
          onClick={() => setMenu((current) => (current === 'help' ? undefined : 'help'))}
        >
          ?
        </button>
      </div>
      {menu === 'add' && (
        <div role="menu" aria-label="Новый блок" className="board-card board-popover board-palette">
          {palette.map((group) => (
            <div key={group.title} role="group" aria-label={group.title}>
              <p className="board-palette-title">{group.title}</p>
              {group.items.map((item) => (
                <button
                  key={item.key}
                  type="button"
                  role="menuitem"
                  draggable
                  onDragStart={(event) => {
                    event.dataTransfer.setData(blockMime, item.key);
                    event.dataTransfer.effectAllowed = 'copy';
                  }}
                  onClick={() => addFree(item.key)}
                >
                  {item.label}
                </button>
              ))}
            </div>
          ))}
        </div>
      )}
      {menu === 'help' && (
        <p className="board-card board-popover board-help">
          Нажмите на выход блока «+», чтобы добавить следующий блок, или тяните от него провод к
          входу «когда»; провод, отпущенный в пустоте, тоже предложит новый блок. Отдельный блок
          добавляется кнопкой «+» слева. Блок двигается за шапку и удаляется крестиком или Delete.
        </p>
      )}
      {error && (
        <p role="alert" className="board-toast">
          {error}
        </p>
      )}
      {picker && (
        <div
          role="dialog"
          aria-label="Добавить блок"
          className={`board-card flowchart-picker${pickerAt ? ' is-placed' : ''}`}
          style={pickerAt}
        >
          <div className="flowchart-picker-head">
            <span>После «{eventExitLabel(picker.port) || 'событие'}»</span>
            <button type="button" aria-label="Закрыть" onClick={closePicker}>
              ×
            </button>
          </div>
          <button type="button" onClick={() => insert('condition')}>
            Если
          </button>
          <hr />
          {actionDefinitions.map((definition) => (
            <Fragment key={definition.key}>
              {definition.key === 'cancel' && <hr />}
              <button type="button" onClick={() => insert(definition.key)}>
                {definition.label}
              </button>
            </Fragment>
          ))}
        </div>
      )}
      <div
        ref={canvas}
        className="flowchart-canvas"
        aria-label="Холст блок-схемы"
        onDragOver={(event) => {
          if (event.dataTransfer.types.includes(blockMime)) {
            event.preventDefault();
            event.dataTransfer.dropEffect = 'copy';
          }
        }}
        onDrop={(event) => {
          const type = event.dataTransfer.getData(blockMime);
          if (type) {
            event.preventDefault();
            addFree(type, { x: event.clientX, y: event.clientY });
          }
        }}
      >
        <ReactFlow<Node<FlowchartNodeData>, Edge<FlowchartWireData>>
          nodes={flowNodes}
          edges={flowEdges}
          nodeTypes={nodeTypes}
          edgeTypes={edgeTypes}
          connectOnClick={false}
          connectionDragThreshold={4}
          nodesDraggable
          nodesConnectable
          elementsSelectable
          edgesReconnectable
          deleteKeyCode={['Delete', 'Backspace']}
          panOnDrag
          zoomOnScroll
          zoomOnPinch
          zoomOnDoubleClick={false}
          minZoom={0.3}
          maxZoom={2}
          fitView
          fitViewOptions={fitOptions}
          onInit={setInstance}
          onNodesChange={onNodesChange}
          onNodeClick={(_, node) => editor.select(node.id)}
          onPaneClick={() => {
            setMenu(undefined);
            closePicker();
          }}
          onConnectEnd={(event, state) => {
            // A wire let go over empty board offers the blocks it can lead to, placed there.
            if (state.isValid || state.toNode || !state.fromNode || !state.fromHandle?.id) {
              return;
            }
            if (state.fromHandle.type !== 'source') {
              return;
            }
            const point = 'changedTouches' in event ? event.changedTouches[0]! : event;
            setPicker({ nodeId: state.fromNode.id, port: state.fromHandle.id });
            setDrop(instance?.screenToFlowPosition({ x: point.clientX, y: point.clientY - 20 }));
            const box = canvas.current?.getBoundingClientRect();
            if (box) {
              placedByDrop.current = true;
              setPickerAt(menuPlace(box, point.clientX + 12, point.clientY - 18));
            }
          }}
          onNodeDragStop={(_, node) => {
            editor.execute(moveBlocks({ [node.id]: node.position }));
            setDragging((current) => {
              const next = { ...current };
              delete next[node.id];
              return next;
            });
          }}
          onEdgeMouseEnter={(_, edge) => setHoveredEdge(edge.id)}
          onEdgeMouseLeave={() => setHoveredEdge(undefined)}
          onEdgesDelete={(edges) => {
            change(disconnectWires(edges.map((edge) => edge.id)));
          }}
          isValidConnection={(connection) => isValidConnection(connection, definition)}
          onConnect={(connection) => {
            if (!connection.sourceHandle) {
              return;
            }
            change(
              connectBlocks(
                { nodeId: connection.source, port: connection.sourceHandle },
                connection.target,
              ),
            );
          }}
          onReconnectStart={() => {
            reconnected.current = false;
          }}
          onReconnect={(old, connection) => {
            const edge = projectedEdge(old.id);
            if (!edge || !connection.sourceHandle) {
              return;
            }
            reconnected.current = true;
            change(
              reconnectWire(
                edge.id,
                { nodeId: connection.source, port: connection.sourceHandle },
                connection.target,
              ),
            );
          }}
          onReconnectEnd={(_, old) => {
            // Letting go of a wire end in empty space removes the wire.
            const edge = projectedEdge(old.id);
            if (!reconnected.current && edge) {
              change(disconnectWire(edge.id));
            }
          }}
          colorMode="dark"
          attributionPosition="bottom-left"
        >
          <Background variant={BackgroundVariant.Dots} color="#353d50" gap={24} size={1.4} />
          <Panel position="bottom-right">
            <ZoomBar />
          </Panel>
        </ReactFlow>
      </div>
    </section>
  );
}
