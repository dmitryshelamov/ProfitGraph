/** Which trade cards a preset's pairs offer (ADR 0069); `Both` offers every card. */
export type ScanTradeDirection = 'Both' | 'Long' | 'Short';

/** The side a pair's page opens its trade cards on, from the preset that found the pair. */
export type ScanTradeSide = 'Long' | 'Short';

/** A side from the address if it is one. */
export function scanTradeSide(value: unknown): ScanTradeSide | undefined {
  return value === 'Long' || value === 'Short' ? value : undefined;
}

/** The side a preset's pairs open on; none for a preset that trades both ways. */
export function presetTradeSide(direction: ScanTradeDirection): ScanTradeSide | undefined {
  return direction === 'Both' ? undefined : direction;
}
