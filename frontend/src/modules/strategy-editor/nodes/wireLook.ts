import type { FlowchartNode } from '../model/blockPresentation';
import { eventExitLabel } from '../model/blockPresentation';
import { flowchartTone } from './FlowchartNode';

export interface WireLook {
  readonly color: string;
  /** Order and cancel exits fire later, once the exchange answers, so their wires are dashed. */
  readonly dashed: boolean;
  readonly label: string;
}

const colors = {
  event: '#8a96ab',
  yes: '#3cc184',
  no: '#ef5350',
  decision: '#e0a93a',
  buy: '#3cc184',
  sell: '#f07a5c',
  cancel: '#d968a3',
  step: '#5b93f0',
};

/** A wire takes its look from the exit it leaves: an event, a yes or no branch, or an order status. */
export function wireLook(source: FlowchartNode, port: string): WireLook {
  const tone = flowchartTone(source);
  const label = eventExitLabel(port) || source.title;
  if (tone === 'event') {
    return { color: colors.event, dashed: false, label };
  }
  if (tone === 'decision') {
    return {
      color: port === 'no' ? colors.no : port === 'yes' ? colors.yes : colors.decision,
      dashed: false,
      label,
    };
  }
  return { color: colors[tone], dashed: tone !== 'step', label };
}
