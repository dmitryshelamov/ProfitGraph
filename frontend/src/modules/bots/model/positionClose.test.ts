import { closeOutcome, closeSteps, isActiveClose } from './positionClose';
import type { BotPositionClose } from './types';

const close: BotPositionClose = {
  id: 'c1',
  botId: 'b1',
  stopAfter: true,
  status: 'Cancelling',
  cancelledOrders: 3,
  engineOrderId: null,
  failureReason: null,
  createdAt: '2026-10-30T10:00:00Z',
  resolvedAt: null,
};

describe('position close', () => {
  it('follows the steps of a close and stops last when asked', () => {
    expect(closeSteps(close)).toEqual([
      { label: 'Бот принимает запрос', state: 'done' },
      { label: 'Отмена ордеров бота: 3', state: 'active' },
      { label: 'Продажа по рынку', state: 'waiting' },
      { label: 'Остановка бота', state: 'waiting' },
    ]);
    expect(
      closeSteps({ ...close, status: 'Closed', stopAfter: false }).map((item) => item.state),
    ).toEqual(['done', 'done', 'done']);
  });

  it('tells a working close from an ended one and words the outcome', () => {
    expect(isActiveClose(close)).toBe(true);
    expect(isActiveClose({ status: 'Closed' })).toBe(false);
    expect(closeOutcome({ ...close, status: 'Closed' })).toBe('Позиция закрыта, бот остановлен.');
    expect(closeOutcome({ ...close, status: 'Failed', failureReason: 'InsufficientBase' })).toBe(
      'Закрыть позицию не удалось: не хватает актива. Бот остановлен.',
    );
    expect(closeOutcome({ ...close, status: 'Failed', failureReason: 'expired' })).toContain(
      'бот не принял запрос за минуту',
    );
  });
});
