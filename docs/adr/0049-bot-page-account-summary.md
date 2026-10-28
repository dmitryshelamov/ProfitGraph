# ADR 0049: The Bot Page Starts with a Summary of Its Exchange Account

Status: Proposed, 2026-10-28. Builds on the account value snapshots (stage 5.14, Profit Fox Designer ADR 0069), the
credentials of ADR 0015, ADR 0022 (C7, account protection) and ADR 0023 (TC5, account selection). The owner reviews it in
the pull request.

## Context

A bot trades through one encrypted credential, and the Worker already reads that credential's account value every five
minutes (`account_value_snapshots`, `account_balance_status`). The owner sees it only on the keys page, one key at a time,
and the bot page does not say which exchange account the bot trades on or how much money is there. The owner asked for the
bot page to begin with that: which account (the Steam account, the T-Invest account, the Bybit UID and the key's note), the
account's total and available value with its coins, and other cheap facts about the key and the account.

What is stored today names the account only partly: a credential has a label, the key's last four characters and a public
identifier (`exchange_credentials.public_id`), which is the SteamID for Steam, the account number for T-Invest and the API key
for Bybit. The T-Invest account's name and kind are read when the credential is created (`POST
/api/exchange-credentials/accounts`) and not kept, and Bybit's UID and key note are never read: only Bybit's read-only `GET
/v5/user/query-api` reports them, with the key's permissions and expiry.

## Decision

- **One query, stored data only.** `GET /api/bots/{id}/account` (`GetBotAccountQuery`) returns the summary of the owner's bot;
  another user's bot answers `404 bot_not_found`, like every bot endpoint. It reads the database and the venue modules' own
  stored state (`IVenueAccountStatus`, `IVenueAccountProtection`), never a venue, so a page view costs no venue request.
- **What it returns.** The credential's label and last four characters; the account's public identifier when it names the
  account (the venue's `IVenueCredentialSchema.PublicField` is not the API key), so the Bybit API key never appears; the
  cached identity below; the latest snapshot (total, available, currency, coins) with the last attempt, success, failure
  reason and next attempt of the balance reads; the venue status and protection; the bot's `allocation_cap` and every other
  bot of the same credential that should run, with its symbol and capital. Money is exact decimal strings and times UTC.
- **Cached identity.** A new Application port `IVenueAccountIdentityReader` describes the credential's account: its
  identifier, name or key note, kind, the key's access level, read-only flag, permissions and expiry. It reports failures as
  reason codes and never returns a key, token or secret. Bybit reads it from `GET /v5/user/query-api` (UID, note, main or
  sub account, read-only or read-write, permissions as `Group:Name`, `expiredAt`); the key, secret and IP list in that answer
  are never read. T-Invest reads it from `UsersService/GetAccounts`, filtered to the credential's account (number, name,
  brokerage account or IIS, full access or read-only); an account the token no longer sees is a failure. Steam has no reader:
  its SteamID is the public identifier, and the Guard status and protection already describe the account.
- **Refresh.** The `AccountSnapshotHost` of the Worker's live role reads the identity of every credential with a bot on a
  venue that has a reader, after the balances: once a day after a success and an hour after a failure, which keeps the last
  details. The answer is the `AccountIdentity` aggregate in `account_identities` (migration `202610280040`), one row per
  credential, deleted with it.
- **Card.** `BotAccountCard` (`modules/bots`) comes first on the bot page, under the title: the account's identifier, name,
  kind and access; the total and available value with the snapshot's age (warning color after fifteen minutes); the bot's
  capital and how many other bots of the key run; why the last read failed and whether the protection holds the account back.
  Coins, the key's permissions and expiry, the venue status, the other bots with their capital and the read schedule are
  behind a "Подробнее" disclosure. The page polls the summary every minute; a failed load says so quietly and the rest of the
  page works.

Storing the identity as new columns of `exchange_credentials` was considered. A credential changes only its label after
creation (ADR 0015), and the identity changes on the venue's side (a renamed note, a new permission, an expiry), so it is a
read cache with its own schedule and failures, like `account_balance_status`, not part of the credential.

## Consequences

- One more read-only request per credential and day reaches Bybit and T-Invest; nothing reaches a venue per page view, and a
  credential without a bot is never read.
- The bot's capital and the other bots' capital are each in their symbol's quote currency, and the account value is in the
  venue's valuation currency (USD for Bybit), so the card shows them side by side and adds nothing up.
- A credential stored before this change shows its account identifier at the first identity read, within a minute of the
  Worker's start; until then the card shows what the credential stores.
- The Steam account name is not stored by the Steam module, so the card names a Steam account by its SteamID.
