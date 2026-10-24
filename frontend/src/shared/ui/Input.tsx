import type { ComponentProps } from 'react';

export function Input({ className = '', ...props }: ComponentProps<'input'>) {
  return (
    <input
      {...props}
      className={`w-full rounded-md border border-line-strong bg-panel px-3 py-2 text-text outline-none placeholder:text-muted focus:border-link ${className}`.trim()}
    />
  );
}
