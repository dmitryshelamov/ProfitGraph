/** An address is shown only when it is plain https; the server builds it, but it is not trusted blindly. */
export function safeImageUrl(url: string | null | undefined): string | null {
  return url && /^https:\/\/[^\s"'<>]+$/.test(url) ? url : null;
}

/** Public icon sets keyed by ticker; a ticker without an icon falls back to the plate. */
export function cryptoIconUrl(baseAsset: string | null | undefined): string | null {
  const ticker = baseAsset?.trim().toLowerCase() ?? '';

  return /^[a-z0-9]{1,12}$/.test(ticker)
    ? `https://cdn.jsdelivr.net/gh/spothq/cryptocurrency-icons@master/128/color/${ticker}.png`
    : null;
}

/** A six-hex-digit color as CSS, otherwise nothing. */
export function hexColor(value: string | null | undefined): string | undefined {
  return value && /^[0-9A-Fa-f]{6}$/.test(value) ? `#${value}` : undefined;
}

/** Steam symbols are "{currency}:{appId}:{market name}"; the market name is what people know the item by. */
export function assetTitle(venue: string, symbol: string): string {
  return venue === 'steam' ? symbol.split(':').slice(2).join(':') || symbol : symbol;
}
