const checks: Record<string, string> = {
  OrderSizePreviewValid: 'Размер ордера допустим',
  PositionFlat: 'Позиция закрыта',
  PositionFlatPreview: 'Остаток позиции',
  NoActiveOrders: 'Нет активных ордеров',
  NoActiveTaggedOrder: 'Нет ордера с меткой',
  HasBaseBalance: 'Есть базовый баланс',
  Compare: 'Сравнение',
  CrossesAbove: 'Пересечение вверх',
  CrossesBelow: 'Пересечение вниз',
};

export function nodeTypeTitle(key: string): string {
  return checks[key] ?? key;
}

const fields: Record<string, string> = {
  minQuantity: 'Минимальное количество актива',
  period: 'Период',
  field: 'Поле',
  op: 'Операция',
  side: 'Сторона',
  type: 'Тип ордера',
  tag: 'Метка ордера',
  amount: 'Сумма в котируемой валюте',
  price: 'Цена',
  quantity: 'Количество актива',
  orderId: 'ID ордера',
  enabled: 'Разрешено',
  name: 'Имя параметра',
  value: 'Значение',
  symbol: 'Торговая пара',
  timeframe: 'Таймфрейм',
  exchange: 'Биржа',
  trigger: 'Событие запуска',
  condition: 'Условие',
  event: 'Событие',
  in: 'Вход',
  out: 'Выход',
  a: 'Первое значение',
  b: 'Второе значение',
  submitted: 'Отправлено',
  filled: 'Исполнено',
  cancelled: 'Отменено',
  rejected: 'Отклонено',
  completed: 'Завершено',
  valid: 'Допустима',
  reason: 'Причина отказа',
  exists: 'Существует',
  order: 'Ордер',
  normalizedQuantity: 'Количество после округления',
  requiredQuote: 'Требуется в котируемой валюте',
  requiredBase: 'Требуется актива',
  quoteTotal: 'Всего в котируемой валюте',
  quoteAvailable: 'Доступно в котируемой валюте',
  quoteReserved: 'Занято в котируемой валюте',
  baseTotal: 'Всего актива',
  baseAvailable: 'Доступно актива',
  baseReserved: 'Занято актива',
  costBasis: 'Стоимость позиции',
  averageCost: 'Средняя цена покупки',
  lastBuyPrice: 'Цена последней покупки',
  barsHeld: 'Свечей в позиции',
  realizedPnl: 'Реализованная прибыль',
  reservedQuote: 'Занято в котируемой валюте',
  reservedBase: 'Занято актива',
  grossBase: 'Количество до комиссии',
  baseChange: 'Изменение количества актива',
  quoteChange: 'Изменение суммы',
  feeBase: 'Комиссия в активе',
  feeQuote: 'Комиссия в котируемой валюте',
  attemptId: 'ID попытки',
  multiplier: 'Число отклонений',
  fastPeriod: 'Быстрый период',
  slowPeriod: 'Медленный период',
  signalPeriod: 'Период сигнальной линии',
  upper: 'Верхняя полоса',
  middle: 'Средняя линия',
  lower: 'Нижняя полоса',
  macd: 'Линия MACD',
  signal: 'Сигнальная линия',
  histogram: 'Гистограмма MACD',
};

const valueTypes: Record<string, string> = {
  Integer: 'Целое число',
  Scalar: 'Число без единицы',
  Price: 'Цена',
  BaseAmount: 'Количество актива',
  QuoteAmount: 'Сумма в валюте пары',
  Boolean: 'Да или нет',
  Tag: 'Метка ордера',
  OrderId: 'ID ордера',
  RejectionReason: 'Причина отказа',
  OrderSide: 'Сторона ордера',
  OrderType: 'Тип ордера',
  Event: 'Событие',
  Numeric: 'Число',
  Order: 'Ордер',
  Fill: 'Сделка',
  Rejection: 'Отклонение',
  CancelResult: 'Результат отмены',
  Candle: 'Свеча',
  Start: 'Начало',
};

const allowedValues: Record<string, string> = {
  Market: 'Рыночный',
  Limit: 'Лимитный',
  Buy: 'Покупка',
  Sell: 'Продажа',
  Filled: 'Исполнен',
  PartiallyFilled: 'Частично исполнен',
  Cancelled: 'Отменён',
  Rejected: 'Отклонён',
  Any: 'Любая',
  Close: 'Закрытие',
  Open: 'Открытие',
  High: 'Максимум',
  Low: 'Минимум',
  Volume: 'Объём',
  Add: 'Сложение',
  Subtract: 'Вычитание',
  Multiply: 'Умножение',
  Divide: 'Деление',
  Min: 'Минимум',
  Max: 'Максимум',
  Modulo: 'Остаток от деления',
};

export function fieldLabel(key: string, catalogLabel?: string | null): string {
  return fields[key] ?? (catalogLabel?.trim() || key);
}
export function portLabel(key: string, catalogLabel?: string | null): string {
  return fields[key] ?? (catalogLabel?.trim() || key);
}
export function valueTypeLabel(key: string): string {
  return valueTypes[key] ?? key;
}
export function allowedValueLabel(key: string): string {
  if (allowedValues[key]) {
    return allowedValues[key];
  }
  const [source, field, extra] = key.split('.');
  const eventSources: Record<string, string> = {
    Candle: 'Свеча',
    Fill: 'Сделка',
    Order: 'Ордер',
    Rejection: 'Отклонение',
    CancelResult: 'Результат отмены',
  };
  if (source && field && !extra && eventSources[source]) {
    const firstLower = field.charAt(0).toLocaleLowerCase() + field.slice(1);
    return `${eventSources[source]}: ${allowedValues[field] ?? fields[firstLower] ?? field}`;
  }
  return key;
}
export function valueTypeHint(key: string): string {
  return key === 'Integer'
    ? 'Целое число, например период индикатора.'
    : key === 'Scalar'
      ? 'Число без денежной или ценовой единицы.'
      : key === 'Price'
        ? 'Цена одного актива в валюте пары.'
        : key === 'BaseAmount'
          ? 'Количество покупаемого или продаваемого актива.'
          : key === 'QuoteAmount'
            ? 'Сумма в валюте пары, например в USDT.'
            : key === 'Boolean'
              ? 'Логическое значение: да или нет.'
              : '';
}
