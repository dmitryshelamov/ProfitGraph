import {
  canCancelOpenOrder,
  cancelProblemLabel,
  openOrderPrice,
  openOrderQuantity,
  openOrderState,
  openOrderStatusLabel,
  orderCancelState,
} from './openOrders';

describe('open orders', () => {
  it('shows a limit order by its quantity and price', () => {
    expect(openOrderQuantity({ quantity: '832.75', quoteBudget: null })).toBe('832.75');
    expect(openOrderPrice({ type: 'Limit', limitPrice: '0.03872' })).toBe('0.03872');
  });

  it('shows a market buy by its budget at the market price', () => {
    expect(openOrderQuantity({ quantity: null, quoteBudget: '30' })).toBe('на 30');
    expect(openOrderPrice({ type: 'Market', limitPrice: null })).toBe('по рынку');
  });

  it('names the states an open order can be in', () => {
    expect(openOrderStatusLabel('Active')).toBe('Стоит на бирже');
    expect(openOrderStatusLabel('PendingSubmit')).toBe('Отправляется');
    expect(openOrderStatusLabel('PendingCancel')).toBe('Отменяется');
  });

  it('calls an active order that is held back deferred, with its reason', () => {
    const waiting = [
      {
        engineOrderId: 2,
        code: 'SettlementHold',
        low: null,
        high: null,
        until: '2026-11-02T00:00:00Z',
      },
    ] as const;

    expect(openOrderState({ engineOrderId: 2, status: 'Active' }, waiting)).toEqual({
      label: 'Отложен',
      reason: 'Ждёт снятия трейд-холда до 02.11.2026, 00:00:00 UTC',
    });
    expect(openOrderState({ engineOrderId: 3, status: 'Active' }, waiting)).toEqual({
      label: 'Стоит на бирже',
      reason: null,
    });
    expect(openOrderState({ engineOrderId: 2, status: 'PendingCancel' }, waiting).label).toBe(
      'Отменяется',
    );
  });

  it('offers a cancel for an order at the venue or being sent, not for one already cancelling', () => {
    expect(canCancelOpenOrder({ status: 'Active' })).toBe(true);
    expect(canCancelOpenOrder({ status: 'PendingSubmit' })).toBe(true);
    expect(canCancelOpenOrder({ status: 'PendingCancel' })).toBe(false);
  });

  it('follows an owner’s cancel until the order is gone or the request is rejected', () => {
    const cancel = { engineOrderId: 2, rejectionReason: null };

    expect(orderCancelState({ ...cancel, status: 'Pending' }, [{ engineOrderId: 2 }])).toEqual({
      text: 'Отмена ордера 2 ждёт бота…',
      settled: false,
    });
    expect(
      orderCancelState({ ...cancel, status: 'Requested' }, [{ engineOrderId: 2 }]).settled,
    ).toBe(false);
    expect(orderCancelState({ ...cancel, status: 'Requested' }, [{ engineOrderId: 3 }])).toEqual({
      text: 'Ордер 2 больше не активен: биржа его отменила или он успел исполниться (см. ленту).',
      settled: true,
    });
    expect(
      orderCancelState({ engineOrderId: 2, status: 'Rejected', rejectionReason: 'expired' }, []),
    ).toEqual({ text: 'Отмена ордера 2 отклонена: бот не взял отмену за минуту.', settled: true });
  });

  it('names why a cancel was refused, and shows an unknown code as it is', () => {
    expect(cancelProblemLabel('order_cancel_pending')).toBe('ордер уже отменяется');
    expect(cancelProblemLabel('bot_not_running')).toBe('бот не работает');
    expect(cancelProblemLabel('something_new')).toBe('something_new');
  });
});
