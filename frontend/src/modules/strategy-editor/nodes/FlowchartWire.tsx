import { BaseEdge, type Edge, EdgeLabelRenderer, type EdgeProps } from '@xyflow/react';

import type { LayoutPoint } from '../model/flowchartLayout';

export type FlowchartWireData = Record<string, unknown> & {
  readonly points?: readonly LayoutPoint[];
  /** The exit the wire leaves, shown on the wire while it is hovered. */
  readonly label?: string;
  readonly showLabel?: boolean;
  readonly color?: string;
};

/** The point halfway along a polyline. */
function middle(points: readonly LayoutPoint[]): LayoutPoint {
  const lengths = points
    .slice(1)
    .map((point, index) => Math.hypot(point.x - points[index]!.x, point.y - points[index]!.y));
  let rest = lengths.reduce((sum, length) => sum + length, 0) / 2;
  for (const [index, length] of lengths.entries()) {
    if (rest <= length && length > 0) {
      const from = points[index]!;
      const to = points[index + 1]!;
      return {
        x: from.x + ((to.x - from.x) * rest) / length,
        y: from.y + ((to.y - from.y) * rest) / length,
      };
    }
    rest -= length;
  }
  return points[0]!;
}

/**
 * A wire drawn along its computed route. The ends follow the real port positions, so a route computed from the block
 * geometry still meets the ports exactly.
 */
export function FlowchartWire({
  id,
  sourceX,
  sourceY,
  targetX,
  targetY,
  data,
  style,
  interactionWidth,
}: EdgeProps<Edge<FlowchartWireData>>) {
  const route = data?.points;
  const points =
    route && route.length >= 2
      ? route.map((point, index) => {
          if (index === 0) {
            return { x: sourceX, y: sourceY };
          }
          if (index === route.length - 1) {
            return { x: targetX, y: targetY };
          }
          // The runs out of the exit and into the input stay level with their ports.
          if (index === 1 && point.y === route[0]!.y) {
            return { x: point.x, y: sourceY };
          }
          if (index === route.length - 2 && point.y === route[route.length - 1]!.y) {
            return { x: point.x, y: targetY };
          }
          return point;
        })
      : [
          { x: sourceX, y: sourceY },
          { x: targetX, y: targetY },
        ];
  const path = points.map((point, index) => `${index ? 'L' : 'M'}${point.x},${point.y}`).join(' ');
  const at = data?.showLabel && data.label ? middle(points) : null;
  return (
    <>
      <BaseEdge path={path} style={style} interactionWidth={interactionWidth} />
      {at && (
        <EdgeLabelRenderer>
          <span
            className="flowchart-wire-label"
            data-wire={id}
            style={{
              transform: `translate(-50%, -50%) translate(${at.x}px, ${at.y}px)`,
              borderColor: data?.color,
              color: data?.color,
            }}
          >
            {data?.label}
          </span>
        </EdgeLabelRenderer>
      )}
    </>
  );
}
