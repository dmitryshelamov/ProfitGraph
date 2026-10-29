import type { StatusTone } from '@/shared/ui/StatusBadge';

import { rejectionLabel } from './manualOrder';
import type { Bot, BotAlertKind, BotFeedKind, BotRuntimeStatus, BotStatusReason } from './types';

const runtimeLabels: Record<BotRuntimeStatus, string> = {
  Created: 'Ожидает запуска',
  Starting: 'Запускается',
  Running: 'Работает',
  Degraded: 'Остановлен по безопасности',
  Stopped: 'Остановлен',
};

const reasonLabels: Record<BotStatusReason, string> = {
  RuntimeFailure: 'сбой выполнения, бот перезапустится',
  NoGateway: 'торговля в этом процессе выключена',
  UnknownVenue: 'неизвестная биржа',
  IntentFailed: 'заявку не удалось доставить',
  UnresolvedOrder: 'состояние заявки на бирже не установлено',
  ForeignOrder: 'на символе есть чужие заявки',
  KeyPermissions: 'у ключа нет нужных прав',
  UnsupportedFeeAsset: 'комиссия в неподдерживаемой валюте',
  InsufficientBalance: 'недостаточно средств',
  HistoryDataCorrection: 'площадка исправила историю свечей, бот ждёт владельца',
  VenueAccountProtection: 'аккаунт площадки под защитой, сначала проверьте его',
  Killed: 'аварийная остановка',
  DailyLossLimit: 'достигнут дневной лимит убытка',
  VenuePositionMode: 'на бирже не тот режим позиций: нужен hedge mode',
};

const diagnosticLabels: Record<string, string> = {
  StaleFeed: 'нет свежих свечей',
  HeldSubmit: 'заявки удержаны до запуска',
  ExchangeUnavailable: 'биржа не отвечает',
  GraphChangeRejected: 'новая версия стратегии не принята',
};

const alertLabels: Record<BotAlertKind, string> = {
  Degraded: 'Остановлен по безопасности',
  Killed: 'Аварийная остановка',
  RiskRejection: 'Заявка отклонена лимитом',
  ExchangeUnavailable: 'Биржа не отвечает',
  GraphChangeRejected: 'Версия стратегии не принята',
};

/** The bot's state for people: what it does now and, when it stopped by itself, why. */
export function botStatusLabel(
  bot: Pick<Bot, 'desiredState' | 'runtimeStatus' | 'statusReason'>,
): string {
  const status =
    bot.desiredState === 'Running'
      ? runtimeLabels[bot.runtimeStatus]
      : bot.runtimeStatus === 'Created'
        ? 'Не запущен'
        : 'Остановлен';

  return bot.statusReason ? `${status}: ${reasonLabels[bot.statusReason]}` : status;
}

export function botStatusTone(bot: Pick<Bot, 'desiredState' | 'runtimeStatus'>): StatusTone {
  if (bot.runtimeStatus === 'Degraded') {
    return 'danger';
  }

  return bot.desiredState === 'Running' && bot.runtimeStatus === 'Running' ? 'success' : 'neutral';
}

export function diagnosticLabel(diagnostic: string): string {
  return diagnosticLabels[diagnostic] ?? diagnostic;
}

export function reasonLabel(reason: string): string {
  return reason in reasonLabels ? reasonLabels[reason as BotStatusReason] : reason;
}

/** A feed entry's reason: a rejected order names why the order was rejected, other entries why the bot stopped. */
export function feedReasonLabel(kind: BotFeedKind, reason: string): string {
  return kind === 'OrderRejected' ? rejectionLabel(reason) : reasonLabel(reason);
}

export function alertLabel(kind: BotAlertKind): string {
  return alertLabels[kind];
}

/** Parameters and the allocation change only while the bot is stopped; limits change at any time. */
export function canEditParameters(bot: Pick<Bot, 'desiredState' | 'runtimeStatus'>): boolean {
  return (
    bot.desiredState === 'Stopped' &&
    bot.runtimeStatus !== 'Starting' &&
    bot.runtimeStatus !== 'Running'
  );
}

const feedLabels: Record<BotFeedKind, string> = {
  Started: 'Запущен',
  Stopped: 'Остановлен',
  Killed: 'Аварийная остановка',
  Degraded: 'Остановлен по безопасности',
  Recovered: 'Восстановился',
  RuntimeStopped: 'Выполнение остановлено',
  RunRestarted: 'Перезапущен',
  GraphVersionApplied: 'Новая версия стратегии',
  GraphChangeRejected: 'Версия стратегии не принята',
  FeedStalled: 'Нет свежих свечей',
  FeedRecovered: 'Свечи снова поступают',
  ExchangeUnavailable: 'Биржа не отвечает',
  SubmitHeld: 'Заявки удержаны',
  OrderPlaced: 'Заявка отправлена',
  OrderRejected: 'Заявка отклонена',
  OrderFailed: 'Заявку не удалось доставить',
  OrderAbsenceConfirmed: 'Владелец подтвердил: заявки на бирже нет',
  OrderAbsenceProven: 'Заявка закрыта автоматически',
  Fill: 'Исполнение',
  PositionOpened: 'Позиция открыта',
  PositionClosed: 'Позиция закрыта',
  SettingsChanged: 'Настройки изменены',
  FeeCorrected: 'Комиссия уточнена',
  OrderWaiting: 'Заявка ждёт',
  OrderWaitingEnded: 'Заявка больше не ждёт',
  HistoryRestated: 'История свечей пересчитана площадкой',
  OrderCancelRequested: 'Отмена ордера по запросу владельца',
};

export function feedLabel(kind: BotFeedKind): string {
  return feedLabels[kind];
}
