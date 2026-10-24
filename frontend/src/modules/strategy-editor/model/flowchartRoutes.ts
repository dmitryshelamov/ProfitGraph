import type { FlowchartEdge, FlowchartNode } from './blockPresentation';
import {
  HEAD_HEIGHT,
  inputPort,
  type LayoutPoint,
  nodeSize,
  PORT_ROW,
  PORTS_PADDING,
} from './flowchartLayout';

/** Clearance a wire keeps around every block. */
const MARGIN = 14;
/** Straight run out of an exit and into an input before a wire may turn. */
const STUB = 20;
/** Distance between wires that share a channel. */
const SPACING = 8;
/** Extra length a bend costs, so wires prefer few corners. */
const BEND = 60;
/** Extra cost per pixel of running along another wire, so wires do not merge into one line. */
const SHARED = 3;

interface Rect {
  readonly left: number;
  readonly top: number;
  readonly right: number;
  readonly bottom: number;
}

/** Centre of a port row: the node border, the head and half the padding come first. */
function portY(node: FlowchartNode, position: LayoutPoint, port: string): number {
  const row = port === inputPort(node) ? 0 : Math.max(0, node.outputs.indexOf(port));
  return position.y + 1 + HEAD_HEIGHT + PORTS_PADDING / 2 + PORT_ROW * (row + 0.5);
}

const inside = (rect: Rect, x: number, y: number) =>
  x > rect.left && x < rect.right && y > rect.top && y < rect.bottom;

/** Index of the first item greater than or equal to the value in a sorted list. */
function lowerBound(values: readonly number[], value: number): number {
  let low = 0;
  let high = values.length;
  while (low < high) {
    const middle = (low + high) >> 1;
    if (values[middle]! < value) {
      low = middle + 1;
    } else {
      high = middle;
    }
  }
  return low;
}

/** Binary heap of search states ordered by cost. */
class Queue {
  private readonly items: [number, number][] = [];
  get size() {
    return this.items.length;
  }
  push(cost: number, state: number) {
    const items = this.items;
    items.push([cost, state]);
    let index = items.length - 1;
    while (index > 0) {
      const parent = (index - 1) >> 1;
      if (items[parent]![0] <= cost) {
        break;
      }
      items[index] = items[parent]!;
      index = parent;
    }
    items[index] = [cost, state];
  }
  pop(): [number, number] {
    const items = this.items;
    const top = items[0]!;
    const last = items.pop()!;
    if (items.length) {
      let index = 0;
      for (;;) {
        const left = index * 2 + 1;
        const right = left + 1;
        let smallest = index;
        let cost = last[0];
        if (left < items.length && items[left]![0] < cost) {
          smallest = left;
          cost = items[left]![0];
        }
        if (right < items.length && items[right]![0] < cost) {
          smallest = right;
        }
        if (smallest === index) {
          break;
        }
        items[index] = items[smallest]!;
        index = smallest;
      }
      items[index] = last;
    }
    return top;
  }
}

// Directions: 0 right, 1 down, 2 left, 3 up.
const DX = [1, 0, -1, 0];
const DY = [0, 1, 0, -1];

/** Drops points in the middle of straight runs. */
function simplify(points: LayoutPoint[]): LayoutPoint[] {
  const result: LayoutPoint[] = [];
  for (const point of points) {
    const last = result[result.length - 1];
    if (last && last.x === point.x && last.y === point.y) {
      continue;
    }
    const before = result[result.length - 2];
    if (
      before &&
      last &&
      ((before.x === last.x && last.x === point.x) || (before.y === last.y && last.y === point.y))
    ) {
      result[result.length - 1] = point;
    } else {
      result.push(point);
    }
  }
  return result;
}

/**
 * Routes every wire with right angles around the blocks, as in a hand-drawn block diagram. A wire leaves its exit to
 * the right and enters its input from the left, keeps clear of every block, prefers few corners and avoids running
 * along other wires. Wires that still share a channel are then spread apart, except wires from the same exit, which
 * stay one trunk.
 */
export function routeFlowchart(
  nodes: readonly FlowchartNode[],
  edges: readonly FlowchartEdge[],
  positions: Readonly<Record<string, LayoutPoint>>,
): Record<string, LayoutPoint[]> {
  const byId = new Map(nodes.map((node) => [node.id, node]));
  const rects: Rect[] = nodes.map((node) => {
    const position = positions[node.id]!;
    const size = nodeSize(node);
    return {
      left: position.x - MARGIN,
      top: position.y - MARGIN,
      right: position.x + size.width + MARGIN,
      bottom: position.y + size.height + MARGIN,
    };
  });
  const ends = edges
    .filter((edge) => byId.has(edge.source) && byId.has(edge.target))
    .map((edge) => {
      const source = byId.get(edge.source)!;
      const target = byId.get(edge.target)!;
      const from = positions[edge.source]!;
      const to = positions[edge.target]!;
      const start = { x: from.x + nodeSize(source).width, y: portY(source, from, edge.sourcePort) };
      const end = { x: to.x, y: portY(target, to, edge.targetPort) };
      return {
        edge,
        group: `${edge.source}:${edge.sourcePort}`,
        start,
        end,
        out: { x: start.x + MARGIN + STUB / 2, y: start.y },
        in: { x: end.x - MARGIN - STUB / 2, y: end.y },
      };
    });
  const xs = [
    ...new Set([
      ...rects.flatMap((rect) => [rect.left, rect.right]),
      ...ends.flatMap((item) => [item.out.x, item.in.x]),
    ]),
  ].sort((a, b) => a - b);
  const ys = [
    ...new Set([
      ...rects.flatMap((rect) => [rect.top, rect.bottom]),
      ...ends.flatMap((item) => [item.out.y, item.in.y]),
    ]),
  ].sort((a, b) => a - b);
  const width = xs.length;
  const height = ys.length;
  const blocked = (x: number, y: number) => rects.some((rect) => inside(rect, x, y));
  const free = new Uint8Array(width * height);
  for (let i = 0; i < width; i++) {
    for (let j = 0; j < height; j++) {
      free[j * width + i] = blocked(xs[i]!, ys[j]!) ? 0 : 1;
    }
  }
  // A step between neighbouring grid lines is open unless its middle lies inside a block.
  const open = new Uint8Array(width * height * 2);
  for (let i = 0; i < width; i++) {
    for (let j = 0; j < height; j++) {
      const cell = j * width + i;
      if (i + 1 < width) {
        open[cell * 2] = blocked((xs[i]! + xs[i + 1]!) / 2, ys[j]!) ? 0 : 1;
      }
      if (j + 1 < height) {
        open[cell * 2 + 1] = blocked(xs[i]!, (ys[j]! + ys[j + 1]!) / 2) ? 0 : 1;
      }
    }
  }
  const step = (cell: number, direction: number): number => {
    const i = cell % width;
    const j = (cell - i) / width;
    const ni = i + DX[direction]!;
    const nj = j + DY[direction]!;
    if (ni < 0 || nj < 0 || ni >= width || nj >= height) {
      return -1;
    }
    const edgeCell = direction === 0 || direction === 1 ? cell : nj * width + ni;
    return open[edgeCell * 2 + (direction % 2)] ? nj * width + ni : -1;
  };
  // Grid steps already taken, by the exit whose wire took them.
  const used = new Map<number, Set<string>>();
  const stepKey = (cell: number, direction: number) => {
    const next = step(cell, direction);
    return Math.min(cell, next) * 2 + (direction % 2);
  };
  const routes: Record<string, LayoutPoint[]> = {};
  const order = [...ends].sort(
    (a, b) =>
      Math.abs(a.end.x - a.start.x) +
      Math.abs(a.end.y - a.start.y) -
      (Math.abs(b.end.x - b.start.x) + Math.abs(b.end.y - b.start.y)),
  );
  for (const item of order) {
    const startCell = lowerBound(ys, item.out.y) * width + lowerBound(xs, item.out.x);
    const endCell = lowerBound(ys, item.in.y) * width + lowerBound(xs, item.in.x);
    const cost = new Float64Array(width * height * 4).fill(Infinity);
    const previous = new Int32Array(width * height * 4).fill(-1);
    const queue = new Queue();
    cost[startCell * 4] = 0;
    queue.push(0, startCell * 4);
    let found = -1;
    while (queue.size) {
      const [current, state] = queue.pop();
      if (current > cost[state]!) {
        continue;
      }
      const cell = state >> 2;
      const direction = state & 3;
      if (cell === endCell) {
        // The wire must end heading right, into the input.
        if (direction === 0) {
          found = state;
          break;
        }
        const turned = endCell * 4;
        if (current + BEND < cost[turned]!) {
          cost[turned] = current + BEND;
          previous[turned] = state;
          queue.push(current + BEND, turned);
        }
        continue;
      }
      for (let next = 0; next < 4; next++) {
        if (next === (direction + 2) % 4) {
          continue;
        }
        const target = step(cell, next);
        if (target < 0 || (!free[target] && target !== endCell)) {
          continue;
        }
        const i = cell % width;
        const j = (cell - i) / width;
        const ti = target % width;
        const tj = (target - ti) / width;
        const length = Math.abs(xs[ti]! - xs[i]!) + Math.abs(ys[tj]! - ys[j]!);
        const others = used.get(stepKey(cell, next));
        const shared = others && [...others].some((group) => group !== item.group) ? SHARED : 0;
        const total = current + length * (1 + shared) + (next === direction ? 0 : BEND);
        const nextState = target * 4 + next;
        if (total < cost[nextState]!) {
          cost[nextState] = total;
          previous[nextState] = state;
          queue.push(total, nextState);
        }
      }
    }
    if (found < 0) {
      // Nothing free between the blocks: fall back to a plain step.
      const middle = (item.out.x + item.in.x) / 2;
      routes[item.edge.id] = simplify([
        item.start,
        { x: middle, y: item.start.y },
        { x: middle, y: item.end.y },
        item.end,
      ]);
      continue;
    }
    const cells: number[] = [];
    for (let state = found; state >= 0; state = previous[state]!) {
      const cell = state >> 2;
      if (cells[cells.length - 1] !== cell) {
        cells.push(cell);
      }
    }
    cells.reverse();
    for (let index = 0; index + 1 < cells.length; index++) {
      const cell = cells[index]!;
      const next = cells[index + 1]!;
      const direction = next === cell + 1 ? 0 : next === cell - 1 ? 2 : next > cell ? 1 : 3;
      const key = stepKey(cell, direction);
      const groups = used.get(key) ?? new Set<string>();
      groups.add(item.group);
      used.set(key, groups);
    }
    routes[item.edge.id] = simplify([
      item.start,
      ...cells.map((cell) => ({ x: xs[cell % width]!, y: ys[Math.floor(cell / width)]! })),
      item.end,
    ]);
  }
  spread(routes, ends);
  return routes;
}

interface Segment {
  readonly id: string;
  readonly group: string;
  /** Index of the segment's first point in its route. */
  readonly index: number;
  readonly at: number;
  readonly from: number;
  readonly to: number;
}

/**
 * Wires of different exits that still run on the same line are moved apart, so each stays readable. The first and
 * last runs are fixed to their ports and never move.
 */
function spread(
  routes: Record<string, LayoutPoint[]>,
  ends: readonly { edge: FlowchartEdge; group: string }[],
) {
  const groups = new Map(ends.map((item) => [item.edge.id, item.group]));
  for (const vertical of [true, false]) {
    const segments: Segment[] = [];
    for (const [id, points] of Object.entries(routes)) {
      for (let index = 0; index + 1 < points.length; index++) {
        const a = points[index]!;
        const b = points[index + 1]!;
        const fixed = index === 0 || index + 2 === points.length;
        if (fixed || (vertical ? a.x !== b.x : a.y !== b.y)) {
          continue;
        }
        segments.push({
          id,
          group: groups.get(id) ?? id,
          index,
          at: vertical ? a.x : a.y,
          from: Math.min(vertical ? a.y : a.x, vertical ? b.y : b.x),
          to: Math.max(vertical ? a.y : a.x, vertical ? b.y : b.x),
        });
      }
    }
    const lines = new Map<number, Segment[]>();
    for (const segment of segments) {
      lines.set(segment.at, [...(lines.get(segment.at) ?? []), segment]);
    }
    for (const line of lines.values()) {
      // Segments on one line that overlap form a bundle; every exit in a bundle gets its own lane.
      line.sort((a, b) => a.from - b.from);
      let bundle: Segment[] = [];
      let reach = -Infinity;
      const flush = () => {
        const lanes = [...new Set(bundle.map((segment) => segment.group))].sort((a, b) => {
          const first = bundle.find((segment) => segment.group === a)!;
          const second = bundle.find((segment) => segment.group === b)!;
          return first.from - second.from || first.to - second.to;
        });
        if (lanes.length > 1) {
          for (const segment of bundle) {
            const lane = lanes.indexOf(segment.group);
            const shift = (lane - (lanes.length - 1) / 2) * SPACING;
            const points = routes[segment.id]!;
            for (const index of [segment.index, segment.index + 1]) {
              const point = points[index]!;
              points[index] = vertical
                ? { x: point.x + shift, y: point.y }
                : { x: point.x, y: point.y + shift };
            }
          }
        }
        bundle = [];
        reach = -Infinity;
      };
      for (const segment of line) {
        if (segment.from >= reach) {
          flush();
        }
        bundle.push(segment);
        reach = Math.max(reach, segment.to);
      }
      flush();
    }
  }
}
