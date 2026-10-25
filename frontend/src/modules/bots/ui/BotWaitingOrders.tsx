import { useQuery } from '@tanstack/react-query';

import { Card } from '@/shared/ui/Card';
import { DateTime } from '@/shared/ui/DateTime';

import { botWaitingOrdersQuery } from '../api/botsApi';
import { waitingText } from '../model/orderWaiting';

/**
 * Working orders the venue holds locally instead of at the venue, with why (a price band, a closed session, a carry-over to
 * the next session). Shown only while there are any.
 */
export function BotWaitingOrders({ botId }: { botId: string }) {
  const waiting = useQuery(botWaitingOrdersQuery(botId));

  if (!waiting.data || waiting.data.length === 0) {
    return null;
  }

  return (
    <Card title="Заявки ждут">
      <ul className="space-y-2 text-sm" aria-label="Ожидающие заявки">
        {waiting.data.map((order) => (
          <li key={order.engineOrderId} className="flex flex-wrap gap-x-3">
            <span className="font-mono">заявка {order.engineOrderId}</span>
            <span>{waitingText(order)}</span>
            <span className="text-muted">
              с <DateTime value={order.since} />
            </span>
          </li>
        ))}
      </ul>
    </Card>
  );
}
