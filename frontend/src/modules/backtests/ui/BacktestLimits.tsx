import { useQuery } from '@tanstack/react-query';

import { venuesQuery } from '@/modules/market-data';

import { backtestLimitTexts } from '../model/backtestLimits';

/** The venue's own list of what its backtests approximate; nothing for a venue whose replay is exact. */
export function BacktestLimits({ venue }: { venue: string }) {
  const venues = useQuery(venuesQuery());
  const limits = backtestLimitTexts(venues.data?.find((item) => item.id === venue)?.backtestLimits);

  if (limits.length === 0) {
    return null;
  }

  return (
    <div className="text-sm text-muted" aria-label="Ограничения бэктеста">
      <p>Это приближение:</p>
      <ul className="list-disc pl-5">
        {limits.map((limit) => (
          <li key={limit}>{limit}</li>
        ))}
      </ul>
    </div>
  );
}
