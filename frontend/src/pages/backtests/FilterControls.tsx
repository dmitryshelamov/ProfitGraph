import type { ReactNode } from 'react';

import { Field } from '@/shared/ui/Field';
import { Input } from '@/shared/ui/Input';

const selectClass = 'rounded-md border border-line-strong bg-panel px-3 py-2 text-sm text-text';

export interface FilterOption {
  value: string;
  label: string;
  count?: number;
}

function optionText(option: FilterOption): string {
  return option.count === undefined ? option.label : `${option.label} (${option.count})`;
}

export function SearchFilter({
  value,
  onChange,
}: {
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <Field label="Поиск" className="min-w-48 flex-1">
      <Input
        type="search"
        value={value}
        placeholder="Инструмент или стратегия"
        spellCheck={false}
        onChange={(event) => onChange(event.target.value)}
      />
    </Field>
  );
}

/** A filter picked from a list; the first entry, "all", filters nothing. A chosen value missing from the list stays. */
export function SelectFilter({
  label,
  value,
  options,
  allLabel = 'Все',
  onChange,
}: {
  label: string;
  value: string | undefined;
  options: readonly FilterOption[];
  allLabel?: string;
  onChange: (value: string | undefined) => void;
}) {
  const shown =
    value && !options.some((option) => option.value === value)
      ? [...options, { value, label: value }]
      : options;

  return (
    <Field label={label}>
      <select
        className={selectClass}
        value={value ?? ''}
        onChange={(event) => onChange(event.target.value || undefined)}
      >
        <option value="">{allLabel}</option>
        {shown.map((option) => (
          <option key={option.value} value={option.value}>
            {optionText(option)}
          </option>
        ))}
      </select>
    </Field>
  );
}

/** One of a few values shown at once as toggles. */
export function ChoiceFilter({
  label,
  value,
  options,
  onChange,
}: {
  label: string;
  value: string;
  options: readonly FilterOption[];
  onChange: (value: string) => void;
}) {
  return (
    <div className="flex flex-wrap items-center gap-1.5">
      <span className="mr-1 text-sm text-text-secondary">{label}</span>
      <div role="radiogroup" aria-label={label} className="flex flex-wrap gap-1.5">
        {options.map((option) => {
          const checked = option.value === value;

          return (
            <button
              key={option.value}
              type="button"
              role="radio"
              aria-checked={checked}
              onClick={() => onChange(option.value)}
              className={`rounded-md border px-2.5 py-1 text-sm transition-colors ${
                checked
                  ? 'border-link bg-selected text-text'
                  : 'border-line-strong text-text-secondary hover:text-text'
              }`}
            >
              {option.label}
              {option.count !== undefined && (
                <>
                  {' '}
                  <span className="ml-0.5 text-muted">{option.count}</span>
                </>
              )}
            </button>
          );
        })}
      </div>
    </div>
  );
}

/** A filter typed as text; an error explains why the value is not applied. */
export function TextFilter({
  label,
  value,
  error,
  type = 'text',
  inputMode,
  onChange,
}: {
  label: ReactNode;
  value: string;
  error?: string | null;
  type?: 'text' | 'date';
  inputMode?: 'decimal';
  onChange: (value: string) => void;
}) {
  return (
    <div className="w-44">
      <Field label={label}>
        <Input
          type={type}
          value={value}
          inputMode={inputMode}
          aria-invalid={error ? true : undefined}
          onChange={(event) => onChange(event.target.value)}
        />
      </Field>
      {error && <p className="mt-1 text-xs text-danger-text">{error}</p>}
    </div>
  );
}
