import type { StrategyDefinition } from '@/modules/strategies';

import { authoringDiagnosticLabel } from './diagnosticPresentation';
import { namedSettingDeletionMessage } from './namedSettingDependencies';

const definition: StrategyDefinition = {
  parameters: [{ name: 'size', valueType: 'QuoteAmount', defaultValue: '10' }],
  values: [],
  blocks: [
    {
      id: 'condition_1a2b3c',
      kind: 'condition',
      displayName: '',
      checks: [{ type: 'Compare', operands: { a: { kind: 'parameter', name: 'size' } } }],
    },
  ],
  wires: [],
};

it('labels an unnamed block and exact check without exposing its generated-looking identity', () => {
  expect(authoringDiagnosticLabel({ blockId: 'condition_1a2b3c', checkIndex: 0 }, definition)).toBe(
    'Условие 1, проверка 1',
  );
  expect(authoringDiagnosticLabel({ blockId: 'removed' }, definition)).toBe('Блок');
  expect(authoringDiagnosticLabel({}, definition)).toBe('');
});

it('uses the same human block label for dependent-setting deletion', () => {
  const message = namedSettingDeletionMessage(definition, 'parameter', 'size');
  expect(message).toContain('Условие 1');
  expect(message).not.toContain('condition_1a2b3c');
});
