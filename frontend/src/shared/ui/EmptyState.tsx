import type { ReactNode } from 'react';

export function EmptyState({ children, action }: { children: ReactNode; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-4 rounded-lg border border-dashed border-line-strong px-6 py-12 text-center text-muted">
      <p>{children}</p>
      {action}
    </div>
  );
}
