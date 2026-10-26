import type { ReactNode } from 'react';

/** `titleAction` sits beside the heading, outside it; `editor` replaces the heading while it is open. */
export function PageHeader({
  title,
  titleAction,
  editor,
  back,
  meta,
  actions,
}: {
  title: ReactNode;
  titleAction?: ReactNode;
  editor?: ReactNode;
  back?: ReactNode;
  meta?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <header className="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div className="min-w-0 space-y-1">
        {back}
        {editor ?? (
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="text-2xl font-semibold break-words">{title}</h1>
            {titleAction}
          </div>
        )}
        {meta && <p className="text-sm text-muted">{meta}</p>}
      </div>
      {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
    </header>
  );
}
