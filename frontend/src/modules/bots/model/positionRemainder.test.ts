import { unknownBotDisplay } from './botDisplay';
import { remainderReason, unsellableRemainder } from './positionRemainder';

const trx = {
  ...unknownBotDisplay,
  basePlaces: 2,
  baseAsset: 'TRX',
  quoteAsset: 'USDT',
  quantityStep: '0.01',
  minQuantity: '0.1',
  minNotional: '1',
};

describe('unsellable remainder', () => {
  it('finds a remainder below the quantity step, as a base fee leaves after a full exit', () => {
    const remainder = unsellableRemainder('0.00574', trx, '0.356');

    expect(remainder).toEqual({ kind: 'step', limit: '0.01' });
    expect(remainderReason(remainder!, 'TRX', 'USDT')).toBe('меньше шага 0.01 TRX, продать нельзя');
  });

  it('finds a remainder below the minimum quantity or the minimum order sum', () => {
    expect(unsellableRemainder('0.05', trx, '0.356')).toEqual({
      kind: 'minQuantity',
      limit: '0.1',
    });
    expect(unsellableRemainder('2.009', trx, '0.356')).toEqual({ kind: 'minNotional', limit: '1' });
    expect(unsellableRemainder('2.009', trx, null)).toBeNull();
  });

  it('keeps a sellable position, no base and unknown rules out of it', () => {
    expect(unsellableRemainder('84.17574', trx, '0.356')).toBeNull();
    expect(unsellableRemainder('0', trx, '0.356')).toBeNull();
    expect(unsellableRemainder('0.00574', unknownBotDisplay, '0.356')).toBeNull();
  });
});
