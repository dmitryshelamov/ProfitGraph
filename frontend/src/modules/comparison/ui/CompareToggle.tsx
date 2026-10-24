import { COMPARISON_LIMIT, type ComparisonItem, itemKey } from '../model/comparisonItems';
import { useComparisonStore } from '../model/comparisonStore';

/** A checkbox that adds a run or a trial to the comparison or removes it. */
export function CompareToggle({ item, label }: { item: ComparisonItem; label: string }) {
  const items = useComparisonStore((state) => state.items);
  const toggle = useComparisonStore((state) => state.toggle);
  const key = itemKey(item);
  const checked = items.some((existing) => itemKey(existing) === key);

  return (
    <input
      type="checkbox"
      aria-label={label}
      checked={checked}
      disabled={!checked && items.length >= COMPARISON_LIMIT}
      onChange={() => toggle(item)}
    />
  );
}
