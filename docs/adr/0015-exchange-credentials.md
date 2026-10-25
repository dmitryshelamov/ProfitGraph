# ADR 0015: Exchange Credentials

Status: Accepted, 2026-10-25.

## Context

Live bots (stage 5) trade through a venue's authenticated API, so ProfitGraph must store users' exchange API keys
before any code calls an authenticated endpoint. Profit Fox Designer ADR 0053 stored them encrypted per value with
AES-256-GCM, configured master keys by identifier and never returned a secret. ProfitGraph keeps that design; ADR 0001
requires the owner to enter credentials again, so nothing is imported. Stage 5 plan decisions S5-7 and S5-8 apply.

## Decision

- **Aggregate.** `ExchangeCredential` (feature `Accounts`) has an identifier, the owning user, a `VenueId`, a label of
  1 to 100 characters, the final four characters of the API key, the encrypted key, the encrypted secret and the
  creation time. It never changes after creation. A bot will take its venue from its credential (Profit Fox Designer
  ADR 0070). The request names the venue; an unregistered venue is `unknown_venue`, there is no default (ADR 0003).
- **Encryption.** Each value is encrypted separately with AES-256-GCM, a fresh random 12-byte nonce and a 16-byte tag.
  The associated data is `{credentialId:D}|{ownerId:D}|{field}` with fields `apiKey` and `apiSecret`, so a value moved
  to another row or field fails to decrypt. Each value stores its master key identifier, nonce, ciphertext and tag.
  `ISecretProtector` is the Application port; `AesGcmSecretProtector` in Infrastructure implements it.
- **Master keys.** `Secrets:MasterKeys:{keyId}` holds base64-encoded 32-byte keys and `Secrets:ActiveKeyId` selects the
  key for new values. They come from user secrets in Development or from the environment, never from a committed
  file. An invalid key is ignored with a warning that names only its identifier. Without an active key the API starts
  and every credential endpoint answers `503 credentials_unavailable`.
- **Rotation.** Add a key under a new identifier and switch `Secrets:ActiveKeyId`; keep the older keys, which still
  decrypt their values. A value whose key is no longer configured is unavailable. Re-encrypting stored values to the
  active key is a later, deliberate operation; until it exists, an old key is never removed.
- **HTTP.** `POST /api/exchange-credentials` takes `venue`, `label`, `apiKey` and `apiSecret` and answers `201` with
  the metadata. `GET /api/exchange-credentials` lists the caller's credentials, newest first. `DELETE
  /api/exchange-credentials/{id}` answers `204`, or `404 exchange_credential_not_found` for an unknown or another
  user's credential. No response contains a key or a secret.
- **Decryption** happens only in `ReadExchangeCredentialSecretsQuery`, which no endpoint sends; the live runtime uses
  it for a venue gateway. Its result keeps the values in memory and prints as redacted.
- **No secret in logs or errors.** Request, command and result types that carry secrets print as redacted, and
  validation messages never repeat submitted values. A test captures the API's verbose log while a credential is
  created and rejected and finds neither value.

## Consequences

- No code calls an authenticated venue endpoint in this change; checking a key's permissions belongs to the order
  gateway (stage 5).
- Deleting a credential that a bot uses becomes a conflict once bots exist.
- Re-encrypting stored values to a new key and the Telegram link on the account arrive with later work.

## Amendment: account value snapshots (stage 5.14)

- **Reader.** A venue's `IAccountBalanceReader` reads an account's value with a credential: the USD-equivalent total, the
  available value when known, and every coin's wallet balance and USD value. It reads balances only and reports any
  failure as a stable reason with a safe detail (an HTTP status or a venue code), never keys, signatures or response
  bodies. Bybit's reads `GET /v5/account/wallet-balance?accountType=UNIFIED` through the read client's allowlist;
  equity is required, a malformed or repeated coin is skipped, decimals may use exponent notation (Profit Fox Designer
  ADR 0069).
- **Polling.** The Worker's `live` role reads every credential with a bot, stopped or running and during a global halt.
  A credential is due five minutes after a success and 5, 10, 20, 40 and then 60 minutes after consecutive failures;
  failures stay per credential. The credential is decrypted only in memory for the read, and no transaction spans it.
- **Storage.** Each read updates `account_balance_status` and, when it observed a value, adds a snapshot to
  `account_value_snapshots` in one transaction. A credential has at most one snapshot per UTC five-minute bucket; a
  failure keeps the last snapshot and success time. History older than seven days is thinned hourly to the latest
  snapshot of each credential and UTC hour, which never expires.
- **Read.** `GET /api/exchange-credentials/{id}/account?from=&to=&limit=` returns the owner's history, oldest first,
  with the reading status; the default range is the last seven days. The history includes deposits, withdrawals and
  price moves; it is not bot profit.

## Amendment: named fields (ADR 0022)

- A credential holds encrypted values by field name in `exchange_credentials.secrets`, a JSON object of key identifier,
  nonce, ciphertext and tag (base64) per field; the associated data still names the field. Migration `202610250020`
  moved every stored key and secret there unchanged, without decrypting them.
- A venue declares its fields through `IVenueCredentialSchema`. `POST /api/exchange-credentials` accepts only venues
  whose fields are exactly `apiKey` and `apiSecret` (Bybit); any other venue answers `400 credential_input_unsupported`
  and brings its own way to create credentials. The four-character public hint stays.
- `ReadExchangeCredentialSecretsQuery` decrypts every field; a venue reads them by name.
