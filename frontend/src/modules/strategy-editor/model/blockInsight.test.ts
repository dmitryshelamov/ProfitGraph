import type { BlockCatalog, StrategyTemplate } from '@/modules/strategies';
import catalogData from '@/test/blockCatalog.fixture.json';
import templateData from '@/test/strategyTemplates.fixture.json';

import { blockValues, checkTokens, formulaText, formulaTokens } from './blockInsight';

const catalog = catalogData as unknown as BlockCatalog;
const ladder = (templateData as unknown as StrategyTemplate[]).find(
  (template) => template.kind === 'Ladder',
)!.definition;

it('keeps named values as names, shows parameter values and brackets nested arithmetic', () => {
  const nextPrice = ladder.values.find((value) => value.name === 'nextPrice')!;
  const tokens = formulaTokens(nextPrice.expression, ladder, catalog);
  expect(formulaText(tokens)).toBe('позиция: последняя цена покупки × (1 − drop 10%)');
  expect(tokens.find((token) => token.kind === 'parameter')).toEqual({
    kind: 'parameter',
    text: 'drop 10%',
  });
});

it('lists a block’s values after the values they are built from', () => {
  const average = ladder.blocks.find((block) => block.id === 'averageConditions')!;
  const values = blockValues(average, ladder);
  expect(values).toContain('nextPrice');
  for (const value of ladder.values) {
    const position = values.indexOf(value.name);
    if (position < 0) {
      continue;
    }
    for (const nested of JSON.stringify(value.expression).matchAll(/"name":"([^"]+)"/g)) {
      const dependency = values.indexOf(nested[1]!);
      if (dependency >= 0) {
        expect(dependency).toBeLessThan(position);
      }
    }
  }
  expect(formulaText(checkTokens(average.checks![0]!, ladder, catalog))).toMatch(/ордер/i);
});

it.each([
  ['takeProfit', null, 'percent', 'takeProfit 0.18%'],
  ['drop', 'Ladder', 'none', 'drop 0.0018'],
] as const)(
  'formats %s in formulas using its explicit display metadata',
  (name, templateKind, displayUnit, expected) => {
    const definition = {
      ...ladder,
      templateKind,
      parameters: [{ name, valueType: 'Scalar', defaultValue: '0.0018', displayUnit }],
    };
    expect(formulaText(formulaTokens({ kind: 'parameter', name }, definition, catalog))).toBe(
      expected,
    );
  },
);
