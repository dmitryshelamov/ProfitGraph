import { cloneElement, type ReactElement, type ReactNode, useId } from 'react';

/** A labelled form control; the label is bound to the control by id. */
export function Field({
  label,
  children,
  className = '',
}: {
  label: ReactNode;
  children: ReactElement<{ id?: string }>;
  className?: string;
}) {
  const generatedId = useId();
  const id = children.props.id ?? generatedId;

  return (
    <div className={`flex flex-col gap-1.5 ${className}`.trim()}>
      <label htmlFor={id} className="text-sm text-text-secondary">
        {label}
      </label>
      {cloneElement(children, { id })}
    </div>
  );
}
