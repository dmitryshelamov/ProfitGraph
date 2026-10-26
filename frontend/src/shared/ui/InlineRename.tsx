import { type ReactNode, useState } from 'react';

import { Button } from './Button';
import { RenameForm } from './RenameForm';

/** Shows `children` (the name as it is displayed) with a rename button; the button swaps both for a rename form. */
export function InlineRename({
  initial,
  label,
  maxLength,
  allowEmpty,
  onSave,
  children,
}: {
  initial: string;
  label: string;
  maxLength: number;
  allowEmpty?: boolean;
  onSave: (name: string) => Promise<unknown>;
  children: ReactNode;
}) {
  const [editing, setEditing] = useState(false);

  if (editing) {
    return (
      <RenameForm
        initial={initial}
        label={label}
        maxLength={maxLength}
        allowEmpty={allowEmpty}
        onSave={onSave}
        onDone={() => setEditing(false)}
      />
    );
  }

  return (
    <span className="inline-flex flex-wrap items-center gap-2">
      {children}
      <Button
        size="sm"
        variant="ghost"
        aria-label={`Переименовать «${initial}»`}
        onClick={() => setEditing(true)}
      >
        Переименовать
      </Button>
    </span>
  );
}
