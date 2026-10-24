export type { ComparisonItem } from './model/comparisonItems';
export {
  COMPARISON_LIMIT,
  comparisonSearch,
  itemKey,
  parseComparison,
} from './model/comparisonItems';
export { useComparisonStore } from './model/comparisonStore';
export { CompareToggle } from './ui/CompareToggle';
export { ComparisonBar } from './ui/ComparisonBar';
export type { ComparisonColumn, ComparisonMetric } from './ui/ComparisonTable';
export { ComparisonTable } from './ui/ComparisonTable';
