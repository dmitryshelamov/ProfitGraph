import { shown } from '../model/format';

/** A rounded decimal with its exact value on hover; `suffix` names the unit. */
export function Amount({
  value,
  places = 2,
  suffix = '',
}: {
  value: string | null | undefined;
  places?: number;
  suffix?: string;
}) {
  const { text, exact } = shown(value, places);

  return (
    <span className="font-mono" title={exact}>
      {text}
      {exact !== undefined && suffix}
    </span>
  );
}
