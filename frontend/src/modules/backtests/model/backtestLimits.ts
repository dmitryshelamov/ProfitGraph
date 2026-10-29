/**
 * What a venue's backtests do not model, by the codes its descriptor reports (ADR 0023: "the run says so"). Unknown codes
 * are skipped rather than shown as raw text.
 */
const texts: Record<string, string> = {
  reference_calendar:
    'Ночи, выходные и пустые интервалы определены по эталонной акции: закрытия рынка не попадают в историю.',
  holiday_sessions: 'Праздничные сессии в будни проигрываются как обычные.',
  price_band: 'Ценовой коридор биржи не моделируется.',
  trading_status: 'Статус торгов инструмента не моделируется.',
  gap_fills_at_limit: 'Лимитная заявка, перекрытая гэпом на открытии, исполняется по своей цене.',
  no_trades_fill_nothing: 'На свече без сделок ордера не исполняются.',
  funding_not_modelled:
    'Фандинг фьючерсов не моделируется: результат не учитывает платежи по позиции.',
};

export function backtestLimitTexts(codes: readonly string[] | undefined): string[] {
  return (codes ?? []).flatMap((code) => (texts[code] ? [texts[code]] : []));
}
