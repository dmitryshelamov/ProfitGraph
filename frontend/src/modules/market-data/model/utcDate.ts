/**
 * Period bounds are whole UTC days: midnight UTC lies on the grid of every supported timeframe, so a chosen day is
 * always a valid bound. The browser's time zone never shifts the chosen day.
 */
export function utcDayStart(day: string): string | null {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(day)) {
    return null;
  }

  const instant = new Date(`${day}T00:00:00Z`);

  return Number.isNaN(instant.getTime()) || instant.toISOString().slice(0, 10) !== day
    ? null
    : instant.toISOString();
}

/** Today's date in UTC, as a date input expects it. */
export function utcToday(now: Date = new Date()): string {
  return now.toISOString().slice(0, 10);
}
