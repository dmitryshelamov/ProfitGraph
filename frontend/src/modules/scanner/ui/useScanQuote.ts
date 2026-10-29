import { useQuery } from '@tanstack/react-query';

import { scanVenuesQuery } from '../api/scannerApi';

/** The currency of the venue's turnover, trade amounts and results (USDT on Bybit, RUB on T-Invest), empty while unknown. */
export function useScanQuote(venue: string): string {
  const venues = useQuery(scanVenuesQuery());

  return venues.data?.find((item) => item.venue === venue)?.quoteAsset ?? '';
}
