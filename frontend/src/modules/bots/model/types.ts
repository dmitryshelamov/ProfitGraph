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
