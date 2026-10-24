import type { BlockCatalog, StrategyBlock, StrategyDefinition } from './types';

const eventLabels: Record<string, string> = {
  OnStart: 'При старте',
  OnResume: 'При возобновлении',
  OnCandleClosed: 'Свеча закрылась',
  OnOrderFilled: 'Ордер исполнен',
  OnOrderCancelled: 'Ордер отменён',
  OnOrderRejected: 'Ордер отклонён',
};
const previousEventLabels: Record<string, string> = {
  OnStart: 'Начало прогона',
  OnResume: 'Возобновление',
  OnCandleClosed: 'Закрытие свечи',
  OnOrderFilled: 'Сделка по ордеру',
  OnOrderCancelled: 'Отмена ордера',
  OnOrderRejected: 'Отклонение ордера',
};

/** Human authoring labels; generated identities are never used as a title. */
export function strategyBlockLabel(
  block: StrategyBlock,
  definition?: StrategyDefinition,
  catalog?: BlockCatalog,
): string {
  const name = block.displayName?.trim();
  if (block.kind === 'event') {
    const source = block.source ?? '';
    if (name && name !== previousEventLabels[source] && name !== eventLabels[source]) {
      return name;
    }
    return (
      catalog?.eventSources.find((entry) => entry.key === source)?.label ||
      eventLabels[source] ||
      'Событие'
    );
  }
  if (name) {
    return name;
  }
  if (block.kind === 'condition') {
    const index = definition?.blocks
      .filter((item) => item.kind === 'condition')
      .findIndex((item) => item.id === block.id);
    return index !== undefined && index >= 0 ? `Условие ${index + 1}` : 'Условие';
  }
  if (block.kind === 'cancel') {
    return 'Отмена ордера';
  }
  return block.orderKind === 'Limit'
    ? `Лимитная ${block.side === 'Sell' ? 'продажа' : 'покупка'}`
    : `${block.side === 'Sell' ? 'Продажа' : 'Покупка'} по рынку`;
}
