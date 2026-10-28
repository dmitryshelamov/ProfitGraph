# ADR 0042: Telegram Notifications

Status: Proposed, 2026-10-27. Replaces the delivery, texts and update handling of ADR 0020; linking, the switch and the
poller lease stay as ADR 0020 describes them. The owner reviews it in the pull request.

## Context

ADR 0020 sends only bot alerts. The owner wants Telegram to be the main channel for a running bot, as in Profit Fox:
lifecycle changes, closed positions and every executed order, with the amounts. A review of `main` found defects in
ADR 0020's delivery:

- One loop polls and delivers: alerts wait for a long poll of up to 25 seconds and stop while `getUpdates` fails.
- Any answer but 403 throws out of the pass, and the next pass starts again from the same oldest alert, so one alert that
  Telegram keeps refusing (a 400, a 429 that is not waited out) blocks every later alert for good.
- The queue is a column of `bot_alerts`, so nothing else can be queued, although ADR 0016 asks side effects outside the
  database to have their own delivery queue fed by a handler.
- Messages name the bot by symbol and timeframe although a bot has a name, and the client reads only text messages, so no
  button can be answered.

The deployment has no public address, so a message cannot link to the bot page; Telegram reaches the Worker only through
long polling.

## Decision

### Amounts in messages

- Messages may carry prices, quantities, amounts, fees and profit (an owner decision that reverses ADR 0020's "never
  amounts"). Keys, tokens, client order identifiers and raw venue or broker answers are still never sent. Telegram stores
  the messages; the owner accepts that.

### Queue

- **Table.** `telegram_notifications` holds one row per message to send: its kind, owner, bot, the alert for an alert,
  details (a JSON object of invariant strings, decimals exact), status, attempts, next attempt, claim and claim expiry,
  the time it was sent and the last error. The chat is not stored; it is read from the owner's link when the row is sent,
  so a chat linked again receives what is still waiting.
- **Feeding.** Outbox handlers in `Application/Notifications` add a row only when the owner has a linked chat that is not
  blocked, in the delivery transaction of the event (ADR 0016), so each event queues its message once. Nothing is queued
  for an owner without a chat, as before.
- **Kinds.** `Alert` for every bot alert (`BotAlertRaised`, raised when an alert is stored, ADR 0018), `Started`,
  `StopRequested` (`BotStopped`), `Stopped` (`BotRuntimeStopped`), `Recovered`, `PositionClosed` (`BotPositionClosed` with the cycle's realized profit) and
  `OrderFilled`.
- **Texts** are built when a row is sent, in Russian, HTML parse mode, every value through `WebUtility.HtmlEncode`. A bot
  is named by its name and, without one, by its symbol and timeframe. Status reasons become fixed Russian texts; an
  unknown code is shown as is.

### Executed orders

- **One message per completely filled order**, as in Profit Fox: partial fills send nothing; the message comes when the
  order is filled. ProfitGraph had no such event: `BotFillRecorded` is raised for every execution, and the final
  notice of an order often adds no quantity (Bybit's order stream reports `Filled` after the execution stream reported
  the last part), so it records no fill at all.
- The new Domain event `BotOrderFilled` is raised by `Bot.RecordOrderFilled` in the root's commit, when the root applied
  a `FinalFill` venue update to an order the checkpoint before it still held and the checkpoint after it no longer does.
  It carries the order's totals from the update's cumulative values (gross base and quote, fee and its asset, as
  `BotFill` records them), the order's realized profit for a sell (the profit of its earlier parts kept in the order's
  progress plus the root's change), the instrument's quantity step, price tick and quote precision for formatting, and
  the order's tag. It is derived only from the checkpoints the runtime already compares; the Engine, replay and every
  pinned hash stay unchanged.
- **Format** (Profit Fox's `CreateOrderFilled`): `✅` for a buy, `🤑` for a sell with profit, `💱` for any other sell;
  `<b>КУПЛЕНО · SYMBOL</b>` or `<b>ПРОДАНО · SYMBOL</b>`; lines «Бот», «Биржа» (the venue descriptor's name), «Рынок»
  (the descriptor's market, when it has one), «Количество» at the quantity step, «Средняя цена исполнения» at the price
  tick, «Потрачено» or «Получено» at the quote precision, «Комиссия» (or «Возврат комиссии» below zero) when it is not
  zero, with a note when it was paid in the base asset; for a sell «Прибыль (PnL): +X» or «Убыток (PnL): X»; hashtags of
  the symbol and the venue (letters, digits and underscores only).
- An order partly filled and then cancelled or expired sends no order message; its fills are in the feed, and a position
  it closes still sends `PositionClosed`.

### Delivery

- **Host.** `TelegramDeliveryHost` runs on every live Worker with Telegram enabled, apart from polling, without a lease.
  Each pass claims up to ten due rows oldest first with `FOR UPDATE SKIP LOCKED`, records a claim identifier and a claim
  expiry 45 seconds ahead and commits; then it sends them one by one outside any transaction and records each outcome in
  a short transaction that checks the claim. A process that dies holding a claim leaves its rows to another process
  after the expiry. Delivery is at least once: a crash between Telegram's acceptance and the record sends the row again.
  The host looks for due rows every second and at once after a pass that sent something.
- **Outcomes.** A 200 is `Sent`. A 403 marks the row `ChatUnavailable` and blocks the chat (ADR 0020). A 400 is a
  permanent refusal: `Failed` with the error. A 429, a 5xx, a timeout or a network failure is temporary: the row waits
  5 s, 15 s, 1 min, 5 min and then 10 min between attempts, or Telegram's `retry_after` when it is longer, and is tried
  again without a limit on attempts. A 401 or 404 (a wrong token) is temporary too, so fixing the token delivers what
  waits. A row whose chat was unlinked or blocked before it was sent is `ChatUnavailable` without a request.
- **Age.** A row is `Skipped` instead of sent when it is older than one hour, or 24 hours for an alert, so a Worker that
  comes back after downtime does not flood the chat with old trades. The application still shows everything.
- **Limits.** One process sends at most one message per second to a chat and 25 per second in all, Telegram's published
  limits; several processes rely on `retry_after`.
- **Errors** keep only our code and Telegram's error description (`http_400: Bad Request: ...`), at most 200 characters;
  never the token or the request.
- **Retention.** Rows finished more than seven days ago are deleted by the host.

### Updates

- **Poller.** `TelegramPollingHost` keeps the leader lease and the stored offset of ADR 0020 and asks for `message` and
  `callback_query` updates. It uses its own `getUpdates(offset)` loop; the offset advances after each update, also after a
  failed one, which is logged, so one bad update cannot stop the bot.
- **Processor.** `TelegramUpdateProcessor` (Application) handles each update in its own scope. It ignores updates from bots
  and from chats that are not private, dispatches a text message to the `ITelegramMessageHandler` of its command
  (`/start CODE` links the chat, `/help` and anything else answer the help text) and a button press to the
  `ITelegramCallbackHandler` of the action before the colon of its data, and always answers a button press
  (`answerCallbackQuery`) so the client stops waiting. Commands from Telegram are MediatR commands like any other.
- **Buttons.** An alert carries one button, «Принято» (`ack:{alertId}`). Pressing it acknowledges the alert through
  `AcknowledgeTelegramAlertCommand`, which finds the user by the linked chat and acknowledges only that user's alert, and
  removes the button from the message. There is no «Открыть бота» button: the deployment has no public address.
- Bot control from Telegram (wave 2) adds handlers to the same processor.

### Client

- The client stays our own `HttpClient` over the Bot API instead of the Telegram.Bot library. ProfitGraph needs four
  methods (`getUpdates`, `sendMessage`, `answerCallbackQuery`, `editMessageReplyMarkup`) and its own offset loop, so the
  library's receiving would go unused; the library puts the token into every request address and wraps transport
  failures in its own exceptions, so the token guarantee would have to be proven again for every version of a package
  that renamed most of its API between major versions. Our client already proves it in tests: the factory's logging is
  removed, no exception carries the address, and a send answers a typed result (`Sent`, `Blocked`, `RetryLater` with
  `retry_after`, `Rejected` with the description) instead of throwing for Telegram's answers.

### Existing alerts

- The migration moves alerts not yet sent, raised in the last 24 hours after their owner's chat was linked, into the queue
  as `Alert` rows, and drops `bot_alerts.telegram_sent_at`. Older unsent alerts would be skipped by the age rule anyway.

## Consequences

- Alerts no longer wait for a long poll, and a refused message never holds up the next one.
- Every executed order, start, stop and closed position of a bot whose owner linked a chat adds a row and an outbox
  message; a busy bot sends many messages, which is what the owner asked for. There are no switches per kind.
- `Telegram:Enabled` stays false in every committed setting; tests use a fake Bot API only.
- Rows queued while Telegram is disabled for a linked chat wait and are skipped by age once it is enabled.
