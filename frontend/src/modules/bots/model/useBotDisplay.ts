import { useQuery } from '@tanstack/react-query';

import { instrumentRulesQuery, instrumentsQuery } from '@/modules/market-data';

import { type BotDisplay, stepPlaces, unknownBotDisplay } from './botDisplay';
import type { Bot } from './types';

/**
 * The venue's decimal places, order minimums and the traded assets of the bot's symbol; unknown until they load or when the
 * venue refuses.
 */
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
    // A Steam item is named by its full market name, which is no unit: its quantity counts pieces.
    baseAsset: bot.venue === 'steam' ? 'шт.' : (instrument?.baseAsset ?? null),
    quoteAsset: instrument?.quoteAsset ?? null,
    quantityStep: rules.data?.quantityStep ?? null,
    minQuantity: rules.data?.minQuantity ?? null,
    minNotional: rules.data?.minNotional ?? null,
  };
}
