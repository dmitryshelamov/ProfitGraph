import { Link } from '@tanstack/react-router';

import { Button, buttonClassName } from '@/shared/ui/Button';

import { comparisonSearch } from '../model/comparisonItems';
import { useComparisonStore } from '../model/comparisonStore';

/** How many runs and trials are picked, with the way to compare them side by side. */
export function ComparisonBar() {
  const items = useComparisonStore((state) => state.items);
  const clear = useComparisonStore((state) => state.clear);

  if (items.length === 0) {
    return null;
  }

  return (
    <aside
      aria-label="Сравнение"
      className="fixed right-4 bottom-4 z-20 flex items-center gap-3 rounded-md border border-line-strong bg-panel-2 px-4 py-2 shadow-lg"
    >
      <span className="text-sm">Выбрано для сравнения: {items.length}</span>
      <Link
        to="/compare"
        search={comparisonSearch(items)}
        className={buttonClassName('primary', 'sm')}
      >
        Сравнить
      </Link>
      <Button size="sm" variant="ghost" onClick={clear}>
        Очистить
      </Button>
    </aside>
  );
}
