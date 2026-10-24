import type { FlowchartBoard, FlowchartEdge, FlowchartNode } from './blockPresentation';

export interface LayoutPoint {
  readonly x: number;
  readonly y: number;
}

export interface NodeSize {
  readonly width: number;
  readonly height: number;
}

export interface FlowchartLayout {
  /** Top-left corner of each block. */
  readonly nodes: Readonly<Record<string, LayoutPoint>>;
  readonly width: number;
  readonly height: number;
}

// Block geometry. The node component sets the same heights, so the layout never has to measure the DOM.
export const NODE_WIDTH = 224;
export const HEAD_HEIGHT = 46;
export const PORT_ROW = 22;
export const PORTS_PADDING = 8;
export const DETAIL_ROW = 19;
export const DETAILS_PADDING = 14;
const COLUMN_GAP = 72;
const ROW_GAP = 40;
const PADDING = 32;

/** The input every non-start block receives its event on. */
export function inputPort(node: FlowchartNode): string | null {
  return node.kind === 'decision' ? 'event' : node.kind === 'action' ? 'trigger' : null;
}

export function nodeSize(node: FlowchartNode): NodeSize {
  const ports = Math.max(inputPort(node) ? 1 : 0, node.outputs.length);
  const details = node.details.length + (node.mode ? 1 : 0);
  return {
    width: NODE_WIDTH,
    height:
      HEAD_HEIGHT +
      ports * PORT_ROW +
      PORTS_PADDING +
      (details ? details * DETAIL_ROW + DETAILS_PADDING : 0),
  };
}

/** Exits in the order the layout follows them: the main path first, "нет" last. */
function exitOrder(node: FlowchartNode): string[] {
  return [
    ...node.outputs.filter((port) => port !== 'no'),
    ...node.outputs.filter((port) => port === 'no'),
  ];
}

/**
 * Lays blocks out on a grid, like a hand-drawn block diagram. Every start sits in the first column, below the previous
 * one, as high as its blocks fit. The main exit ("да", "исполнен", …) continues in the next column on the same row;
 * "нет" drops below its decision; further exits open new rows under everything the block has already placed. A block
 * reached a second time (a merge or an order-event cycle) keeps its first place. Rows are as tall as their tallest
 * block.
 */
export function layoutFlowchart(projection: FlowchartBoard): FlowchartLayout {
  const byId = new Map(projection.nodes.map((node) => [node.id, node]));
  const outgoing = new Map<string, FlowchartEdge[]>();
  for (const edge of projection.edges) {
    outgoing.set(edge.source, [...(outgoing.get(edge.source) ?? []), edge]);
  }
  const cells = new Map<string, { col: number; row: number }>();
  const occupied = new Set<string>();
  // Places one start and everything it reaches first, with rows relative to the start.
  const placeTree = (root: FlowchartNode) => {
    const local = new Map<string, { col: number; row: number }>();
    const place = (node: FlowchartNode, col: number, row: number): number => {
      local.set(node.id, { col, row });
      let bottom = row;
      let sameRowTaken = false;
      for (const port of exitOrder(node)) {
        for (const edge of outgoing.get(node.id)?.filter((item) => item.sourcePort === port) ??
          []) {
          const target = byId.get(edge.target);
          if (!target || cells.has(target.id) || local.has(target.id)) {
            continue;
          }
          if (port === 'no') {
            bottom = Math.max(bottom, place(target, col, bottom + 1));
          } else if (!sameRowTaken) {
            sameRowTaken = true;
            bottom = Math.max(bottom, place(target, col + 1, row));
          } else {
            bottom = Math.max(bottom, place(target, col + 1, bottom + 1));
          }
        }
      }
      return bottom;
    };
    place(root, 0, 0);
    return local;
  };
  // Each next start goes to the highest row below the previous start where its blocks fit into free cells.
  let startRow = -1;
  // Detached blocks follow, those without an incoming arrow first, so a detached chain keeps its order.
  const entered = new Set(projection.edges.map((edge) => edge.target));
  const roots = [
    ...projection.nodes.filter((node) => node.kind === 'start'),
    ...projection.nodes.filter((node) => node.kind !== 'start' && !entered.has(node.id)),
    ...projection.nodes.filter((node) => node.kind !== 'start' && entered.has(node.id)),
  ];
  for (const root of roots) {
    if (cells.has(root.id)) {
      continue;
    }
    const tree = placeTree(root);
    let offset = startRow + 1;
    while ([...tree.values()].some((cell) => occupied.has(`${cell.col}:${cell.row + offset}`))) {
      offset++;
    }
    for (const [id, cell] of tree) {
      cells.set(id, { col: cell.col, row: cell.row + offset });
      occupied.add(`${cell.col}:${cell.row + offset}`);
    }
    startRow = offset;
  }

  const rowHeights: number[] = [];
  for (const [id, cell] of cells) {
    rowHeights[cell.row] = Math.max(rowHeights[cell.row] ?? 0, nodeSize(byId.get(id)!).height);
  }
  const rowTops: number[] = [];
  let top = PADDING;
  for (let row = 0; row < rowHeights.length; row++) {
    rowTops[row] = top;
    top += (rowHeights[row] ?? 0) + ROW_GAP;
  }
  const nodes: Record<string, LayoutPoint> = {};
  let columns = 0;
  for (const [id, cell] of cells) {
    nodes[id] = { x: PADDING + cell.col * (NODE_WIDTH + COLUMN_GAP), y: rowTops[cell.row]! };
    columns = Math.max(columns, cell.col + 1);
  }
  return {
    nodes,
    width: PADDING * 2 + columns * (NODE_WIDTH + COLUMN_GAP) - COLUMN_GAP,
    height: top - ROW_GAP + PADDING,
  };
}

/** The first spot at or below `point` where a block of `size` overlaps none of `taken`. */
export function freeSpot(
  point: LayoutPoint,
  size: NodeSize,
  taken: readonly (LayoutPoint & NodeSize)[],
): LayoutPoint {
  let y = point.y;
  for (;;) {
    const hit = taken.find(
      (box) =>
        point.x < box.x + box.width + 16 &&
        box.x < point.x + size.width + 16 &&
        y < box.y + box.height + 16 &&
        box.y < y + size.height + 16,
    );
    if (!hit) {
      return { x: point.x, y };
    }
    y = hit.y + hit.height + ROW_GAP;
  }
}
