import type { ReactNode } from 'react';

export type StatusTone = 'neutral' | 'success' | 'danger';

const toneClasses: Record<StatusTone, string> = {
  neutral: 'border-line-strong bg-panel-2 text-muted',
  success: 'border-line-strong bg-panel text-up',
  danger: 'border-danger-border bg-danger-bg text-danger-text',
};

const dotClasses: Record<StatusTone, string> = {
  neutral: 'bg-muted',
  success: 'bg-up',
  danger: 'bg-danger-text',
};

export function StatusBadge({ tone, children }: { tone: StatusTone; children: ReactNode }) {
  return (
    <span
      role="status"
      className={`inline-flex items-center gap-2 rounded-full border px-3 py-1 text-sm ${toneClasses[tone]}`}
    >
      <span aria-hidden="true" className={`size-2 rounded-full ${dotClasses[tone]}`} />
      {children}
    </span>
  );
}
