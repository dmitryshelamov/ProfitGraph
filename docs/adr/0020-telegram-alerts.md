# ADR 0020: Telegram Alerts

Status: Accepted, 2026-10-25.

## Context

Bot alerts (ADR 0018) are durable rows the owner reads in the application. An owner also wants them on the phone
without watching the page. Telegram's Bot API offers long polling (`getUpdates`) and `sendMessage`; it allows one
`getUpdates` consumer per bot, and a user who blocks the bot makes every send fail with 403. Fill notifications and
trading from Telegram are out of scope for stage 5.

## Decision

- **Switch.** `Telegram:Enabled` is false in every committed setting. Only a Worker with the `live` role and the switch
  on registers the Telegram client and its host; otherwise nothing is registered and nothing starts. The bot token
  comes from private configuration, is part of every request path, and never reaches a log: the client's factory
  logging is removed and no exception carries the address. A path starts with `./` because a token has a colon.
- **Linking.** A signed-in user asks for a one-time code (`POST /api/profile/telegram/link-code`, ten minutes, the
  user's earlier code replaced; only its SHA-256 hash is stored) and sends `/start CODE` to the bot, directly or by the
  returned `t.me` link when `Telegram:BotUsername` is set. The chat it came from is linked to the user
  (`telegram_links`, one chat per user and one user per chat); the bot answers whether it worked. `GET` shows the
  status and `DELETE` unlinks.
- **Leader.** The live processes elect one poller with a lease (`telegram_poller_lease`, 60 s, renewed every pass). The
  lease keeps the next update offset, advanced after each update, so a new leader continues where the last stopped.
- **Delivery.** In each pass the leader sends, oldest first, the alerts raised after their owner's chat was linked and
  not sent yet, and marks each sent only after Telegram accepted it (`bot_alerts.telegram_sent_at`). A crash between
  the two sends it again: delivery is at least once. A failed send is tried on the next pass. A 403 marks the chat
  blocked; it receives nothing more until it is linked again.
- **Texts.** Messages are in Russian, name the bot by its instrument and timeframe, and carry the alert kind and
  reason, never amounts, keys or venue payloads.

## Consequences

- Alerts raised while no chat was linked are not sent later; the application keeps them.
- One Telegram bot serves the deployment; several Workers with the `live` role share it safely through the lease.
