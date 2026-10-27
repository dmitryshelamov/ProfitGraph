const passed: Record<string, string> = {
  NotFoundByRequestId: 'биржа не находит заявку по номеру запроса',
  NotAmongActiveOrders: 'среди активных заявок счёта по этой бумаге её нет',
  NoExecutionRecorded: 'бот не учёл по заявке ни одного исполнения',
  NotFoundByExchangeId: 'биржа не знает её и по биржевому номеру',
  TradingDayEnded: 'торговый день заявки закончился',
  NoExecutionInOperations: 'в операциях по счёту и бумаге за время жизни заявки исполнений нет',
  PositionMatchesEngine: 'позиция по бумаге равна ожидаемой ботом, ничего не заблокировано',
  NotFoundInOpenAndHistoricalOrders: 'биржа не находит заявку ни среди открытых, ни в истории',
  SettleDelayPassed: 'после отправки заявки прошло больше суток',
  NoExecutionOfOrder: 'в сделках по символу исполнений заявки нет',
  BaseBalanceMatchesEngine: 'баланс монеты равен позиции, которую ожидает бот',
  NoBuyOrderOnTheItem: 'среди заявок на покупку этого предмета в аккаунте её нет',
  NoPurchaseInHistory: 'в истории торговой площадки после отправки заявки покупок предмета нет',
  CancelledByExchange: 'биржа сама сняла заявку, исполненная часть учтена',
  PositionCoversBots: 'позиция по бумаге на счёте не меньше суммы позиций ботов',
};

const failed: Record<string, string> = {
  ActiveOrderFound: 'заявка найдена среди активных на бирже',
  ExecutionRecorded: 'по заявке уже есть исполнение',
  KnownByExchangeId: 'биржа знает заявку по биржевому номеру',
  TradingDayUnknown: 'не удалось определить торговый день заявки',
  TradingDayNotEnded: 'торговый день заявки ещё не закончился или не прошла выдержка после него',
  OrderTooOld: 'заявка слишком старая для автоматической проверки',
  OperationsIncomplete: 'операций по бумаге слишком много, проверить все не удалось',
  ExecutionInOperations: 'в операциях по счёту есть исполнение, возможно частичное',
  ExpectedPositionUnknown: 'бот не знает ожидаемую позицию',
  PositionUnreadable: 'позицию по бумаге биржа сейчас не отдаёт',
  PositionBlocked: 'часть позиции заблокирована заявками',
  PositionDiffers: 'позиция по бумаге не равна ожидаемой ботом',
  PositionShort: 'позиция по бумаге на счёте меньше суммы позиций ботов',
  BotPositionsUnknown: 'не удалось сложить позиции ботов этого счёта',
  ReadFailed: 'биржа не ответила на проверочный запрос',
  AttemptChanged: 'запись о заявке изменилась во время проверки',
  EngineAcceptedOrder: 'бот уже видел, что биржа приняла заявку',
  PlaceStartUnknown: 'неизвестно, когда заявка была отправлена',
  SettleDelayNotPassed: 'с отправки заявки прошло меньше суток',
  ExecutionFound: 'в сделках по символу есть исполнение заявки',
  BalanceDiffers: 'баланс монеты не равен позиции, которую ожидает бот',
  EvidenceFound: 'на площадке есть заявка на покупку или покупка предмета',
  NotProvable: 'такой вид заявки автоматически доказать нельзя',
};

/** The checks that closed an order without the owner, as the feed lists them (comma-separated codes). */
export function absenceProofText(checks: string): string {
  return checks
    .split(',')
    .filter((code) => code.length > 0)
    .map((code) => passed[code] ?? code)
    .join('; ');
}

/**
 * Why the automatic check left an order to the owner, from the stop's detail (`AutoCheck:<code>`), or null for a stop that
 * the automatic check did not write.
 */
export function autoCheckFailure(detail: string | undefined): string | null {
  if (!detail?.startsWith('AutoCheck:')) {
    return null;
  }

  const code = detail.slice('AutoCheck:'.length);

  return failed[code] ?? code;
}
