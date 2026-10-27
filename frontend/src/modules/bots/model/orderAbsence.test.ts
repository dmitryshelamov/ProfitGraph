import { describe, expect, it } from 'vitest';

import { absenceProofText, autoCheckFailure } from './orderAbsence';

describe('absenceProofText', () => {
  it('words every check that closed the order, in order', () => {
    expect(absenceProofText('NotFoundByRequestId,TradingDayEnded,PositionMatchesEngine')).toBe(
      'биржа не находит заявку по номеру запроса; торговый день заявки закончился; позиция по бумаге равна ожидаемой ботом, ничего не заблокировано',
    );
  });

  it('keeps a code it does not know and ignores empty parts', () => {
    expect(absenceProofText('Unknown,,TradingDayEnded')).toBe(
      'Unknown; торговый день заявки закончился',
    );
  });
});

describe('autoCheckFailure', () => {
  it('words the check that stopped the automatic close', () => {
    expect(autoCheckFailure('AutoCheck:PositionDiffers')).toBe(
      'позиция по бумаге не равна ожидаемой ботом',
    );
    expect(autoCheckFailure('AutoCheck:Something')).toBe('Something');
  });

  it('is null for a stop the automatic check did not write', () => {
    expect(autoCheckFailure(undefined)).toBeNull();
    expect(autoCheckFailure('InvalidOperationException: A gap.')).toBeNull();
  });
});
