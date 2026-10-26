import { useQuery } from '@tanstack/react-query';

import { instrumentRulesQuery, instrumentsQuery } from '@/modules/market-data';

import { type BotDisplay, stepPlaces, unknownBotDisplay } from './botDisplay';
import type { Bot } from './types';

/** The venue's decimal places and the traded assets of the bot's symbol; unknown until they load or when the venue refuses. */
export function useBotDisplay(bot: Pick<Bot, 'venue' | 'symbol' | 'credentialId'>): BotDisplay {
  const rules = useQuery(instrumentRulesQuery(bot.venue, bot.symbol, bot.credentialId));
  const instruments = useQuery(instrumentsQuery(bot.venue, bot.symbol, bot.credentialId));
  const instrument = instruments.data?.find((item) => item.symbol === bot.symbol);

  if (!rules.data && !instrument) {
    return unknownBotDisplay;
  }

  return {
    basePlaces: rules.data ? rules.data.basePrecision : null,
    quotePlaces: rules.data ? rules.data.quotePrecision : null,
    pricePlaces: rules.data ? stepPlaces(rules.data.priceTick) : null,
    baseAsset: instrument?.baseAsset ?? null,
    quoteAsset: instrument?.quoteAsset ?? null,
  };
}
