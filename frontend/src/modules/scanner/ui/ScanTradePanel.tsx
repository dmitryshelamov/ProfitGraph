import { useQuery } from '@tanstack/react-query';

import { scanTradeIdeasQuery, scanTradeSettingsQuery } from '../api/scannerApi';
import { ScanPairTrades } from './ScanPairTrades';
import { ScanTradeCards } from './ScanTradeCards';
import { ScanTradeSettingsForm } from './ScanTradeSettingsForm';

/** A pair's trade cards with real orders (ADR 0060): the owner's settings, the cards from the current snapshot and the pair's trades. */
export function ScanTradePanel({ venue, symbol }: { venue: string; symbol: string }) {
  const settings = useQuery(scanTradeSettingsQuery(venue));
  const ideas = useQuery(scanTradeIdeasQuery(venue, symbol));

  return (
    <div className="space-y-4">
      {settings.isSuccess && <ScanTradeSettingsForm venue={venue} settings={settings.data} />}
      {ideas.data ? (
        <ScanTradeCards venue={venue} ideas={ideas.data} />
      ) : (
        <p className="text-sm text-muted">{ideas.isError ? 'Идеи не загрузились.' : 'Загрузка…'}</p>
      )}
      <ScanPairTrades venue={venue} symbol={symbol} />
    </div>
  );
}
