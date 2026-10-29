import { Field } from '@/shared/ui/Field';

import type { LimitFill } from '../model/backtestSetup';

const selectClass =
  'w-full rounded-md border border-line-strong bg-panel px-3 py-2 text-text outline-none focus:border-link';

/**
 * When a resting limit order fills in the backtest (ADR 0057): only once the price trades through it, or already when the
 * price touches it, which is the optimistic case.
 */
export function LimitFillField({
  value,
  onChange,
}: {
  value: LimitFill;
  onChange: (value: LimitFill) => void;
}) {
  return (
    <Field label="Исполнение лимитов">
      <select
        className={selectClass}
        value={value}
        onChange={(event) => onChange(event.target.value as LimitFill)}
      >
        <option value="Through">Пробой: цена прошла лимит</option>
        <option value="Touch">Касание: цена дошла до лимита</option>
      </select>
    </Field>
  );
}
