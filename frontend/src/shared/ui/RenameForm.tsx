import { useMutation } from '@tanstack/react-query';
import { type FormEvent, useState } from 'react';

import { ApiErrorMessage } from './ApiErrorMessage';
import { Button } from './Button';
import { Input } from './Input';

/**
 * A small form for one name. `initial` is what the field starts with: the stored name, empty for an entity that has
 * none. With `allowEmpty` a blank field is saved as an empty name, which clears it. `onDone` runs after a save and on
 * cancel.
 */
export function RenameForm({
  initial,
  label,
  maxLength,
  allowEmpty = false,
  onSave,
  onDone,
}: {
  initial: string;
  /** Names the field for assistive technology, e.g. «Название ключа». */
  label: string;
  maxLength: number;
  allowEmpty?: boolean;
  onSave: (name: string) => Promise<unknown>;
  onDone: () => void;
}) {
  const [draft, setDraft] = useState(initial);
  const save = useMutation({ mutationFn: onSave, onSuccess: onDone });
  const trimmed = draft.trim();
  const canSave = (allowEmpty || trimmed.length > 0) && !save.isPending;

  const submit = (event: FormEvent) => {
    event.preventDefault();

    if (canSave) {
      save.mutate(trimmed);
    }
  };

  return (
    <form onSubmit={submit} className="flex flex-wrap items-center gap-2">
      <Input
        aria-label={label}
        className="w-64 max-w-full"
        value={draft}
        maxLength={maxLength}
        autoFocus
        onChange={(event) => setDraft(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            onDone();
          }
        }}
      />
      <Button type="submit" size="sm" variant="primary" disabled={!canSave}>
        Сохранить
      </Button>
      <Button size="sm" variant="ghost" onClick={onDone}>
        Отмена
      </Button>
      {save.isError && (
        <div className="basis-full">
          <ApiErrorMessage error={save.error} fallback="Не удалось переименовать." />
        </div>
      )}
    </form>
  );
}
