import { describe, expect, it } from 'vitest';

import { absenceProofText, autoCheckFailure } from './orderAbsence';

describe('absenceProofText', () => {
  it('words every check that closed the order, in order', () => {
    expect(absenceProofText('NotFoundByRequestId,TradingDayEnded,PositionMatchesEngine')).toBe(
      'биржа не находит заявку по номеру запроса; торговый день заявки закончился; позиция по бумаге равна ожидаемой ботом, ничего не заблокировано',
    );
  });

  it('words the checks that ended an order the exchange removed with its day', () => {
    expect(absenceProofText('CancelledByExchange,TradingDayEnded,PositionCoversBots')).toBe(
      'биржа сама сняла заявку, исполненная часть учтена; торговый день заявки закончился; позиция по бумаге на счёте не меньше суммы позиций ботов',
    );
  });

  it('words the day end that the Moscow calendar gave when the schedule was unavailable', () => {
    expect(absenceProofText('TradingDayEnded,TradingDayEndedByCalendar')).toBe(
      'торговый день заявки закончился; расписание биржи недоступно, конец дня определён по календарю Москвы (сутки по МСК закончились)',
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
    expect(autoCheckFailure('AutoCheck:PositionShort')).toBe(
      'позиция по бумаге на счёте меньше суммы позиций ботов',
    );
    expect(autoCheckFailure('AutoCheck:Something')).toBe('Something');
  });

  it('is null for a stop the automatic check did not write', () => {
    expect(autoCheckFailure(undefined)).toBeNull();
    expect(autoCheckFailure('InvalidOperationException: A gap.')).toBeNull();
  });
});
