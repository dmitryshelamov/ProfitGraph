import type { StrategyDefinition } from '@/modules/strategies';

import { isValidConnection } from './connectionRules';

const definition: StrategyDefinition = {
  parameters: [],
  values: [],
  blocks: [
    { id: 'start', kind: 'event', source: 'OnStart' },
    { id: 'if', kind: 'condition', join: 'all', checks: [] },
  ],
  wires: [],
};
it('accepts direct event wires and rejects invalid exits or event targets', () => {
  expect(
    isValidConnection(
      { source: 'start', sourceHandle: 'out', target: 'if', targetHandle: 'event' },
      definition,
    ),
  ).toBe(true);
  expect(
    isValidConnection(
      { source: 'start', sourceHandle: 'missing', target: 'if', targetHandle: 'event' },
      definition,
    ),
  ).toBe(false);
  expect(
    isValidConnection(
      { source: 'if', sourceHandle: 'yes', target: 'start', targetHandle: 'event' },
      definition,
    ),
  ).toBe(false);
});
