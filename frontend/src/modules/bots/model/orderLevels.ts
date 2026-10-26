import type { ChartLevel } from '@/modules/market-chart';

import { atPlaces } from './botDisplay';
import { openOrderQuantity } from './openOrders';
import type { BotOpenOrder } from './types';

const sideLabel = { Buy: 'Покупка', Sell: 'Продажа' } as const;

/**
 * The bot's working orders with a limit price as horizontal levels titled with side, price and quantity. A market order
 * has no price to draw.
 */
export function orderLevels(
  orders: readonly BotOpenOrder[],
  pricePlaces: number | null,
): ChartLevel[] {
  return orders.flatMap((order) =>
    order.limitPrice === null
      ? []
      : [
          {
            id: String(order.engineOrderId),
            side: order.side,
            price: order.limitPrice,
            title: `${sideLabel[order.side]} ${atPlaces(order.limitPrice, pricePlaces)} × ${openOrderQuantity(order)}`,
          },
        ],
  );
}
