import { Button } from '@/shared/ui/Button';
import { Field } from '@/shared/ui/Field';
import { Input } from '@/shared/ui/Input';

import type { ScanConditionDraft } from '../model/filter';
import { type ScanMetric, scanMetrics } from '../model/metrics';
import type { PresetDraft } from '../model/presetDraft';

const selectClass =
  'w-full rounded-md border border-line-strong bg-panel px-3 py-2 text-text outline-none focus:border-link';

/**
 * Edits the preset the table shows: its conditions apply to the table at once; saving stores the user's own preset. A built-in
 * preset is edited as a copy.
 */
export function PresetEditor({
  draft,
  onChange,
  onSave,
  onSaveAsNew,
  onDelete,
  saving,
  problem,
}: {
  draft: PresetDraft;
  onChange: (draft: PresetDraft) => void;
  onSave: () => void;
  onSaveAsNew: () => void;
  onDelete: () => void;
  saving: boolean;
  problem: string | null;
}) {
  const nextMetric = scanMetrics.find(
    (definition) => !draft.conditions.some((condition) => condition.metric === definition.metric),
  )?.metric;

  function changeCondition(index: number, condition: ScanConditionDraft) {
    onChange({
      ...draft,
      conditions: draft.conditions.map((item, at) => (at === index ? condition : item)),
    });
  }

  return (
    <div className="space-y-4">
      <Field label="Название">
        <Input
          value={draft.name}
          maxLength={100}
          onChange={(event) => onChange({ ...draft, name: event.target.value })}
        />
      </Field>
      <fieldset className="space-y-2">
        <legend className="mb-1 text-sm text-text-secondary">Условия (границы включительно)</legend>
        {draft.conditions.length === 0 && (
          <p className="text-sm text-muted">Без условий — все пары.</p>
        )}
        {draft.conditions.map((condition, index) => (
          <div
            key={condition.metric}
            className="grid grid-cols-[minmax(0,2fr)_1fr_1fr_auto] items-center gap-2"
          >
            <select
              aria-label="Метрика"
              className={selectClass}
              value={condition.metric}
              onChange={(event) =>
                changeCondition(index, { ...condition, metric: event.target.value as ScanMetric })
              }
            >
              {scanMetrics
                .filter(
                  (definition) =>
                    definition.metric === condition.metric ||
                    !draft.conditions.some((item) => item.metric === definition.metric),
                )
                .map((definition) => (
                  <option key={definition.metric} value={definition.metric}>
                    {definition.label}
                  </option>
                ))}
            </select>
            <Input
              aria-label="От"
              placeholder="от"
              inputMode="decimal"
              value={condition.minimum}
              onChange={(event) =>
                changeCondition(index, { ...condition, minimum: event.target.value })
              }
            />
            <Input
              aria-label="До"
              placeholder="до"
              inputMode="decimal"
              value={condition.maximum}
              onChange={(event) =>
                changeCondition(index, { ...condition, maximum: event.target.value })
              }
            />
            <Button
              size="sm"
              variant="ghost"
              aria-label="Убрать условие"
              onClick={() =>
                onChange({ ...draft, conditions: draft.conditions.filter((_, at) => at !== index) })
              }
            >
              ✕
            </Button>
          </div>
        ))}
        {nextMetric && (
          <Button
            size="sm"
            onClick={() =>
              onChange({
                ...draft,
                conditions: [...draft.conditions, { metric: nextMetric, minimum: '', maximum: '' }],
              })
            }
          >
            Добавить условие
          </Button>
        )}
      </fieldset>
      <div className="grid grid-cols-[minmax(0,2fr)_1fr] items-end gap-2">
        <Field label="Сортировка">
          <select
            className={selectClass}
            value={draft.sortMetric}
            onChange={(event) =>
              onChange({ ...draft, sortMetric: event.target.value as ScanMetric })
            }
          >
            {scanMetrics.map((definition) => (
              <option key={definition.metric} value={definition.metric}>
                {definition.label}
              </option>
            ))}
          </select>
        </Field>
        <label className="flex items-center gap-2 pb-2 text-sm">
          <input
            type="checkbox"
            checked={draft.sortDescending}
            onChange={(event) => onChange({ ...draft, sortDescending: event.target.checked })}
          />
          По убыванию
        </label>
      </div>
      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          checked={draft.hideStale}
          onChange={(event) => onChange({ ...draft, hideStale: event.target.checked })}
        />
        Скрывать пары без сделок
      </label>
      {problem && (
        <p role="alert" className="text-sm text-danger-text">
          {problem}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        <Button variant="primary" disabled={saving || problem !== null} onClick={onSave}>
          {draft.presetId ? 'Сохранить' : 'Сохранить пресет'}
        </Button>
        {draft.presetId && (
          <>
            <Button disabled={saving || problem !== null} onClick={onSaveAsNew}>
              Сохранить как новый
            </Button>
            <Button variant="ghost" disabled={saving} onClick={onDelete}>
              Удалить
            </Button>
          </>
        )}
      </div>
    </div>
  );
}
