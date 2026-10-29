import { useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';

import { scannerKeys } from '../api/scannerApi';
import { followScanSnapshots } from './followScanSnapshots';

/** Reads the venue's snapshot again each time the scanner announces a new one, while the page is open. */
export function useScanSnapshotUpdates(venue: string | undefined) {
  const queryClient = useQueryClient();

  useEffect(() => {
    if (!venue) {
      return undefined;
    }

    return followScanSnapshots(
      () => void queryClient.invalidateQueries({ queryKey: scannerKeys.snapshot(venue) }),
    );
  }, [queryClient, venue]);
}
