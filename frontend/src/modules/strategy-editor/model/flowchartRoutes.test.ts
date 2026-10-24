import type { StrategyTemplate } from '@/modules/strategies';
import templateData from '@/test/strategyTemplates.fixture.json';

import { presentBlocks } from './blockPresentation';
import { layoutFlowchart, nodeSize } from './flowchartLayout';
import { routeFlowchart } from './flowchartRoutes';

const templates = templateData as unknown as StrategyTemplate[];

describe('flowchart wire routes', () => {
  it.each(templates)('$kind wires turn at right angles around the blocks', (template) => {
    const projection = presentBlocks(template.definition);
    const positions = layoutFlowchart(projection).nodes;
    const routes = routeFlowchart(projection.nodes, projection.edges, positions);
    const blocks = projection.nodes.map((node) => ({ ...positions[node.id]!, ...nodeSize(node) }));
    for (const edge of projection.edges) {
      const points = routes[edge.id]!;
      const source = blocks[projection.nodes.findIndex((node) => node.id === edge.source)]!;
      const target = blocks[projection.nodes.findIndex((node) => node.id === edge.target)]!;
      expect(points[0]!.x, edge.id).toBe(source.x + source.width);
      expect(points[points.length - 1]!.x, edge.id).toBe(target.x);
      for (let index = 0; index + 1 < points.length; index++) {
        const a = points[index]!;
        const b = points[index + 1]!;
        expect(a.x === b.x || a.y === b.y, edge.id).toBe(true);
        // Only the first and last runs touch a block: they end on its ports.
        if (index === 0 || index + 2 === points.length) {
          continue;
        }
        for (const block of blocks) {
          const crosses =
            Math.max(a.x, b.x) > block.x &&
            Math.min(a.x, b.x) < block.x + block.width &&
            Math.max(a.y, b.y) > block.y &&
            Math.min(a.y, b.y) < block.y + block.height;
          expect(crosses, `${edge.id} crosses a block`).toBe(false);
        }
      }
    }
  });

  it('keeps wires from different exits off one line', () => {
    const ladder = templates.find((item) => item.kind === 'Ladder')!;
    const projection = presentBlocks(ladder.definition);
    const routes = routeFlowchart(
      projection.nodes,
      projection.edges,
      layoutFlowchart(projection).nodes,
    );
    const runs = projection.edges.flatMap((edge) => {
      const points = routes[edge.id]!;
      // The last run enters a shared input, where wires meet on purpose.
      return points.slice(0, -2).map((a, index) => ({
        exit: `${edge.source}:${edge.sourcePort}`,
        a,
        b: points[index + 1]!,
      }));
    });
    for (const first of runs) {
      for (const second of runs) {
        if (first.exit === second.exit) {
          continue;
        }
        const vertical = first.a.x === first.b.x && second.a.x === second.b.x;
        const horizontal = first.a.y === first.b.y && second.a.y === second.b.y;
        const along = (run: typeof first, axis: 'x' | 'y') =>
          [Math.min(run.a[axis], run.b[axis]), Math.max(run.a[axis], run.b[axis])] as const;
        if (vertical && first.a.x === second.a.x) {
          const [from, to] = along(first, 'y');
          const [otherFrom, otherTo] = along(second, 'y');
          expect(Math.min(to, otherTo) - Math.max(from, otherFrom), first.exit).toBeLessThanOrEqual(
            0,
          );
        }
        if (horizontal && first.a.y === second.a.y) {
          const [from, to] = along(first, 'x');
          const [otherFrom, otherTo] = along(second, 'x');
          expect(Math.min(to, otherTo) - Math.max(from, otherFrom), first.exit).toBeLessThanOrEqual(
            0,
          );
        }
      }
    }
  });
});
