/** A Steam bot's own settings as the form edits them: whole days, a decimal fraction as text, whole seconds. */
export interface SteamBotSettingsDraft {
  readonly settlementHoldDays: string;
  readonly marketBuySlippage: string;
  readonly marketBuyDeadlineSeconds: string;
}

export const steamBotSettingsFallback: SteamBotSettingsDraft = {
  settlementHoldDays: '7',
  marketBuySlippage: '0',
  marketBuyDeadlineSeconds: '120',
};

/** The form's starting values from the venue's defaults, or the module's own when they are missing. */
export function steamBotSettingsDraft(defaults: unknown): SteamBotSettingsDraft {
  if (typeof defaults !== 'object' || defaults === null) {
    return steamBotSettingsFallback;
  }

  const values = defaults as Record<string, unknown>;

  return {
    settlementHoldDays: String(
      values.settlementHoldDays ?? steamBotSettingsFallback.settlementHoldDays,
    ),
    marketBuySlippage: String(
      values.marketBuySlippage ?? steamBotSettingsFallback.marketBuySlippage,
    ),
    marketBuyDeadlineSeconds: String(
      values.marketBuyDeadlineSeconds ?? steamBotSettingsFallback.marketBuyDeadlineSeconds,
    ),
  };
}

/**
 * The `venueSettings` of a new Steam bot: whole numbers as JSON numbers, the slippage as exact decimal text. A value that
 * is not a whole number is sent as text, so the server names the field it refuses.
 */
export function steamBotSettingsBody(draft: SteamBotSettingsDraft): Record<string, unknown> {
  return {
    settlementHoldDays: whole(draft.settlementHoldDays),
    marketBuySlippage: draft.marketBuySlippage.trim(),
    marketBuyDeadlineSeconds: whole(draft.marketBuyDeadlineSeconds),
  };
}

function whole(value: string): number | string {
  const trimmed = value.trim();

  return /^\d{1,9}$/.test(trimmed) ? Number(trimmed) : trimmed;
}
