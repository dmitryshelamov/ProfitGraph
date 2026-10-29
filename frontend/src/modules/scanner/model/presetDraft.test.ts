import { draftInput, draftOf, draftProblem } from './presetDraft';
import type { ScanPreset } from './types';

const builtIn: ScanPreset = {
  id: null,
  venue: 'bybit',
  name: 'Импульс',
  conditions: [{ metric: 'Change1h', minimum: '3', maximum: null }],
  sortMetric: 'Change1h',
  sortDescending: true,
  hideStale: true,
  builtIn: true,
  alertsEnabled: false,
};

describe('preset drafts', () => {
  it('edits a built-in preset as a new copy and sends unset bounds as null', () => {
    const draft = draftOf(builtIn);

    expect(draft.presetId).toBeNull();
    expect(draft.name).toBe('Импульс (копия)');
    expect(draftInput(draft).conditions).toEqual([
      { metric: 'Change1h', minimum: '3', maximum: null },
    ]);
  });

  it('refuses a condition without bounds and a bound that is not an exact decimal', () => {
    const draft = draftOf(builtIn);

    expect(draftProblem(draft)).toBeNull();
    expect(
      draftProblem({ ...draft, conditions: [{ metric: 'Spread', minimum: '', maximum: '' }] }),
    ).toMatch(/границ/);
    expect(
      draftProblem({ ...draft, conditions: [{ metric: 'Spread', minimum: '1,5', maximum: '' }] }),
    ).toMatch(/числа/);
    expect(draftProblem({ ...draft, name: ' ' })).toMatch(/Назовите/);
  });
});
