import { useQuery } from '@tanstack/react-query';

import { botOpenOrdersQuery, botWaitingOrdersQuery } from '../api/botsApi';
import { openOrderPrice, openOrderQuantity, openOrderState } from '../model/openOrders';

export function BotOpenOrders({ botId }: { botId: string }) {
  const orders = useQuery(botOpenOrdersQuery(botId));
  const waiting = useQuery(botWaitingOrdersQuery(botId)).data ?? [];

  if (orders.isError) {
    return (
      <p role="alert" className="text-danger-text">
        Не удалось загрузить активные ордера.
      </p>
    );
  }

  if (!orders.data || orders.data.length === 0) {
    return <p className="text-muted">{orders.isPending ? 'Загрузка…' : 'Активных ордеров нет.'}</p>;
  }

  return (
    <table className="w-full text-left text-sm" aria-label="Активные ордера бота">
      <thead className="text-text-secondary">
        <tr>
          <th className="py-2">Ордер</th>
          <th className="py-2">Сторона</th>
          <th className="py-2">Тип</th>
          <th className="py-2">Количество</th>
          <th className="py-2">Цена</th>
          <th className="py-2">Исполнено</th>
          <th className="py-2">Состояние</th>
        </tr>
      </thead>
      <tbody>
        {orders.data.map((order) => {
          const state = openOrderState(order, waiting);

          return (
            <tr key={order.engineOrderId} className="border-t border-line">
              <td className="py-2">
                <span className="font-mono">{order.engineOrderId}</span>
                {order.tag ? <span className="ml-2 text-muted">{order.tag}</span> : null}
              </td>
              <td className="py-2">{order.side === 'Buy' ? 'Покупка' : 'Продажа'}</td>
              <td className="py-2">{order.type === 'Limit' ? 'Лимитный' : 'Рыночный'}</td>
              <td className="py-2 font-mono">{openOrderQuantity(order)}</td>
              <td className="py-2 font-mono">{openOrderPrice(order)}</td>
              <td className="py-2 font-mono">{order.filledBase}</td>
              <td className="py-2">
                {state.label}
                {state.reason ? <span className="block text-muted">{state.reason}</span> : null}
              </td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}
