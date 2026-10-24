import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';

import { COMPARISON_LIMIT, type ComparisonItem, itemKey } from './comparisonItems';

interface ComparisonState {
  readonly items: readonly ComparisonItem[];
  toggle(item: ComparisonItem): void;
  clear(): void;
}

/**
 * What the user picked to compare, a client-side convenience kept in this browser tab's storage: picking does not
 * change anything on the server, and the comparison itself lives in its address.
 */
export const useComparisonStore = create<ComparisonState>()(
  persist(
    (set) => ({
      items: [],
      toggle: (item) =>
        set((state) => {
          const key = itemKey(item);

          if (state.items.some((existing) => itemKey(existing) === key)) {
            return { items: state.items.filter((existing) => itemKey(existing) !== key) };
          }

          return state.items.length >= COMPARISON_LIMIT ? state : { items: [...state.items, item] };
        }),
      clear: () => set({ items: [] }),
    }),
    { name: 'profitgraph-comparison', storage: createJSONStorage(() => sessionStorage) },
  ),
);
