/**
 * Only an address of this application is a safe place to return to after sign-in. The address is resolved the way the
 * browser resolves it, so `//host`, `/\host`, absolute URLs and other schemes all land on another origin and fall back
 * to the root.
 */
export function safeReturnPath(
  returnTo: string | undefined,
  origin: string = window.location.origin,
): string {
  if (!returnTo) {
    return '/';
  }

  let target: URL;

  try {
    target = new URL(returnTo, origin);
  } catch {
    return '/';
  }

  return target.origin === origin ? `${target.pathname}${target.search}${target.hash}` : '/';
}
