import { formatDateTime } from '../lib/dateTime';

export function DateTime({ value, dateOnly = false }: { value: string; dateOnly?: boolean }) {
  return (
    <time dateTime={value} title={formatDateTime(value)}>
      {formatDateTime(value, dateOnly)}
    </time>
  );
}
