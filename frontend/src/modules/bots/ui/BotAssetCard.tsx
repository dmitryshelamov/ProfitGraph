import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';

import { instrumentsQuery } from '@/modules/market-data';
import { Card } from '@/shared/ui/Card';

import { assetTitle, hexColor, safeImageUrl } from '../model/assetImage';
import type { Bot } from '../model/types';

function AssetImage({
  url,
  fallback,
  background,
}: {
  url: string | null;
  fallback: string;
  background?: string;
}) {
  const [failed, setFailed] = useState(false);

  if (!url || failed) {
    return (
      <div
        aria-hidden="true"
        className="flex size-20 shrink-0 items-center justify-center rounded-lg bg-canvas text-sm font-semibold text-text-secondary"
      >
        {fallback.replace(/[^\p{L}\p{N}]/gu, '').slice(0, 4)}
      </div>
    );
  }

  return (
    <img
      src={url}
      alt=""
      width={80}
      height={80}
      loading="lazy"
      referrerPolicy="no-referrer"
      className="size-20 shrink-0 rounded-lg object-contain"
      style={background ? { background } : undefined}
      onError={() => setFailed(true)}
    />
  );
}

/**
 * The traded asset on the bot page: its picture, name, kind and a short description. The venue's instrument search
 * supplies them (a Steam item's picture and text, a share's logo, a crypto pair's base coin icon). Without a picture or
 * when it fails to load, the card shows the name's letters, and the bot works the same.
 */
export function BotAssetCard({ bot }: { bot: Pick<Bot, 'venue' | 'symbol' | 'credentialId'> }) {
  const instruments = useQuery(instrumentsQuery(bot.venue, bot.symbol, bot.credentialId));
  const instrument = instruments.data?.find((item) => item.symbol === bot.symbol);
  const appearance = instrument?.appearance ?? null;
  const title = assetTitle(bot.venue, bot.symbol);
  const imageUrl = safeImageUrl(appearance?.imageUrl);

  return (
    <Card>
      <section aria-label="Актив" className="flex gap-4">
        <AssetImage
          key={imageUrl ?? ''}
          url={imageUrl}
          fallback={title}
          background={hexColor(appearance?.backgroundColor)}
        />
        <div className="min-w-0 space-y-1">
          <h2 className="font-semibold" style={{ color: hexColor(appearance?.nameColor) }}>
            {title}
          </h2>
          {appearance?.kind && <p className="text-sm text-text-secondary">{appearance.kind}</p>}
          {appearance && appearance.description.length > 0 && (
            <ul className="space-y-0.5 text-sm text-text-secondary">
              {appearance.description.map((line, index) => (
                <li key={index}>{line}</li>
              ))}
            </ul>
          )}
        </div>
      </section>
    </Card>
  );
}
