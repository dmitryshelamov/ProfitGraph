import type { Schemas } from '@/shared/api/client';

export type Bot = Schemas['BotResponse'];
export type BotAlert = Schemas['BotAlertResponse'];
export type BotAlertKind = Schemas['BotAlertKind'];
export type BotRuntimeStatus = Schemas['BotRuntimeStatus'];
export type BotStatusReason = NonNullable<Schemas['BotStatusReason']>;
export type ManualOrder = Schemas['ManualOrderResponse'];
export type OrderSide = Schemas['OrderSide'];
export type TradingHalt = Schemas['TradingHaltResponse'];
export type LiveImpactBot = Schemas['LiveImpactBotResponse'];
export type BotFill = Schemas['BotFillResponse'];
export type BotOpenOrder = Schemas['BotOpenOrderResponse'];
export type OrderStatus = Schemas['OrderStatus'];
export type BotOrderWaiting = Schemas['BotOrderWaitingResponse'];
export type OrderWaitingCode = Schemas['OrderWaitingCode'];
export type BotFeedEntry = Schemas['BotFeedEntryResponse'];
export type BotFeedKind = Schemas['BotFeedKind'];
export type BotPosition = Schemas['BotPositionResponse'];

/** A `LivePrice` hub message (not in the HTTP schema): a venue's display price of an instrument. */
export interface LivePrice {
  readonly venue: string;
  readonly symbol: string;
  readonly price: string;
  readonly observedAt: string;
}
