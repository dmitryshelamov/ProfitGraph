# ADR 0043: Sign-in Throttling and the End of a Blocked Session

Status: Proposed, 2026-10-27. Replaces the per-login sign-in limit of stage 2. The owner reviews it in the pull request.
Two-factor sign-in is out of scope; the owner has not decided on it yet.

## Context

A review of `main` found three weaknesses in sign-in and sessions:

- **Per-login limit.** Every sign-in attempt took a permit from a fixed window of 10 attempts per minute per login,
  successful or not. Anyone who knew the owner's login could keep it locked out with 10 requests a minute, while an
  attacker spread over many client addresses still had 10 guesses a minute, about 14,400 a day, against one login.
- **Return path.** After sign-in the page returned to `returnTo` when it started with `/` and not with `//`. Browsers read
  `/\evil.example` like `//evil.example`, so the check let a crafted link send a user who just signed in to another site.
- **Blocked accounts.** Each HTTP request re-checks that its account is active (`RejectInactiveAccounts`), so blocking
  ends HTTP sessions. A SignalR connection over WebSockets sends no HTTP request after it opens, so a blocked account
  kept receiving backtest and optimization progress, live prices and forming candles until it disconnected.

## Decision

### Lockout after consecutive failures

- **Only failures count.** `SignInThrottle` counts consecutive failed attempts. A successful sign-in clears the count, so
  correct attempts never lock anyone out. An attempt is counted as a failure when it starts and cleared when it succeeds,
  so parallel attempts cannot all pass a lockout that the first of them earns.
- **Growing lockout.** 5 consecutive failures go through unhindered; the 5th and every later failure lock the login out,
  for 30 s after the 5th, doubled with each further failure, up to 15 min. While locked out, an attempt is answered `429 Too Many Requests` with
  `Retry-After` before any password is checked and does not count. A persistent attacker gets 5 guesses and then about
  one every 15 minutes, roughly 100 a day per login instead of 14,400.
- **Two counters.** Failures are counted per login and per login and client address. A client address from which the
  login signed in successfully before (the 4 most recent per login) is held only by its own counter: a stranger who
  fails on the owner's login locks the login everywhere else but not on the addresses the owner signs in from. An IPv6
  client counts as its /64 prefix, since one client usually rotates addresses inside it. Behind a reverse proxy without
  `ReverseProxy:KnownProxies` every client has the proxy's address, and the throttle degrades to one counter per login.
- **Same cost for every login.** Unknown and blocked logins are counted and locked out like existing ones, and still
  cost the same password verification as a wrong password, so neither the answer nor its timing reveals whether an
  account exists.
- **Per-address limit.** The fixed window of 20 sign-in attempts per minute per client address stays.
- **Forgetting.** Failures with no new failure for an hour and no lockout left are forgotten, so the state holds only
  the logins tried in the last hour. Known addresses are added only by a successful sign-in.

### Return path

`safeReturnPath` resolves `returnTo` against the current origin with `new URL(returnTo, window.location.origin)`, the
way the browser would, and returns its path, query and fragment only when the result has the same origin; anything
else, including `//host`, `/\host`, absolute URLs and `javascript:`, returns to `/`.

### Blocked accounts lose their realtime connections

- A global hub filter (`ActiveAccountHubFilter`) registers every hub connection by account in `AccountConnections`.
  Blocking an account through `POST /api/users/{id}/block` closes all of the account's open connections on every hub.
- The filter also checks that the account is active when a connection opens (after registering it, so a block that
  commits meanwhile either finds the connection or is seen by the check) and on every hub method call. A connection that
  a block missed ends on its next call; the call fails with `account_inactive` or with the closed connection.
- New connections are refused like any other request of a blocked account: negotiation requires an active session.

## Consequences

- **One process.** The throttle state and the connection registry live in the API process. A restart forgets failures,
  lockouts and known addresses, which also lets an owner locked out by an attacker sign in again. Several API instances
  would each give an attacker their own budget and would not close each other's connections; they would need the
  counters in shared storage (PostgreSQL or a cache) and a broadcast of blocks, for example through `LISTEN/NOTIFY` as
  progress already uses. The check on hub method calls still ends a missed connection on its next call. The deployment
  runs one API instance.
- **Residual lockout.** An attacker who keeps failing on a login keeps it locked out on every address it has not signed
  in from, at one attempt per 15 minutes. The owner can still sign in from an address used before and keeps sessions
  that are already open. Two-factor sign-in would let the counters be looser; it is the owner's decision.
- **Shared addresses.** Clients behind one address, such as one office NAT, share the per-address counter and the
  known-address exemption.
- Each hub connection and hub method call reads the account once.
