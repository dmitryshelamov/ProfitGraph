/** An address is shown only when it is plain https; the server builds it, but it is not trusted blindly. */
export function safeImageUrl(url: string | null | undefined): string | null {
  return url && /^https:\/\/[^\s"'<>]+$/.test(url) ? url : null;
}

/** A six-hex-digit color as CSS, otherwise nothing. */
export function hexColor(value: string | null | undefined): string | undefined {
  return value && /^[0-9A-Fa-f]{6}$/.test(value) ? `#${value}` : undefined;
}

/**
 * Steam symbols are "{currency}:{appId}:{market name}"; the market name is what people know the item by. T-Invest
 * symbols are "{board}:{ticker}"; the ticker is.
 */
export function assetTitle(venue: string, symbol: string): string {
  if (venue === 'steam') {
    return symbol.split(':').slice(2).join(':') || symbol;
  }

  return venue === 'tinvest' ? symbol.split(':').pop() || symbol : symbol;
}
