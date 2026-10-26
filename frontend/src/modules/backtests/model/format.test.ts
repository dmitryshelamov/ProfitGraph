import { sellerFeesLabel } from './format';

describe('seller fees label', () => {
  it('names the step of seller amounts only when it is coarser than a minor unit', () => {
    const fees = { rates: ['0.05', '0.1'], minimum: '0.01', increment: 1 };

    expect(sellerFeesLabel(fees)).toBe(
      'продавец платит 5% + 10% с каждой единицы, не меньше 0.01 за часть',
    );
    expect(sellerFeesLabel({ ...fees, increment: 100 })).toBe(
      'продавец платит 5% + 10% с каждой единицы, не меньше 0.01 за часть, суммы кратны 100 минимальным единицам валюты',
    );
  });
});
