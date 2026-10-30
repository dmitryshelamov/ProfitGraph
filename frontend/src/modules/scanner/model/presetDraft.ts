import { isDecimalString } from '@/shared/lib/decimal';

import type { ScanConditionDraft, ScanView } from './filter';
import type { ScanMetric } from './metrics';
import type { ScanTradeDirection } from './tradeSide';
import type { ScanPreset, ScanPresetInput } from './types';

/** A preset being edited on the page: its view, its name and which saved preset it came from. */
export interface PresetDraft extends ScanView {
  readonly name: string;
  /** The user's own preset it edits; null for a new one or a copy of a built-in preset. */
  readonly presetId: string | null;
  /** Which trade cards the preset's pairs offer (ADR 0069). */
  readonly tradeDirection: ScanTradeDirection;
}

export function draftOf(preset: ScanPreset): PresetDraft {
  return {
    name: preset.builtIn ? `${preset.name} (копия)` : preset.name,
    presetId: preset.builtIn ? null : (preset.id ?? null),
    conditions: preset.conditions.map((condition) => ({
      metric: condition.metric as ScanMetric,
      minimum: condition.minimum ?? '',
      maximum: condition.maximum ?? '',
    })),
    sortMetric: preset.sortMetric as ScanMetric,
    sortDescending: preset.sortDescending,
    hideStale: preset.hideStale,
    tradeDirection: tradeDirectionOf(preset.tradeDirection),
  };
}

function tradeDirectionOf(value: string): ScanTradeDirection {
  return value === 'Long' || value === 'Short' ? value : 'Both';
}

export const emptyDraft: PresetDraft = {
  name: 'Мой пресет',
  presetId: null,
  conditions: [],
  sortMetric: 'Turnover24h',
  sortDescending: true,
  hideStale: true,
  tradeDirection: 'Both',
};

/** Why the draft cannot be saved, or null; a condition needs a bound, and every bound is an exact decimal. */
export function draftProblem(draft: PresetDraft): string | null {
  if (draft.name.trim() === '') {
    return 'Назовите пресет.';
  }

  for (const condition of draft.conditions) {
    const minimum = condition.minimum.trim();
    const maximum = condition.maximum.trim();

    if (minimum === '' && maximum === '') {
      return 'У каждого условия нужна хотя бы одна граница.';
    }

    if (
      (minimum !== '' && !isDecimalString(minimum)) ||
      (maximum !== '' && !isDecimalString(maximum))
    ) {
      return 'Границы — числа с точкой, например 0.5 или -5.';
    }
  }

  return null;
}

export function draftInput(draft: PresetDraft): ScanPresetInput {
  return {
    name: draft.name.trim(),
    conditions: draft.conditions.map((condition: ScanConditionDraft) => ({
      metric: condition.metric,
      minimum: condition.minimum.trim() === '' ? null : condition.minimum.trim(),
      maximum: condition.maximum.trim() === '' ? null : condition.maximum.trim(),
    })),
    sortMetric: draft.sortMetric,
    sortDescending: draft.sortDescending,
    hideStale: draft.hideStale,
    tradeDirection: draft.tradeDirection,
  };
}
