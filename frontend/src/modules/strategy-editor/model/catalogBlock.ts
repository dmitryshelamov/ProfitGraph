import type { StrategyBlock } from '@/modules/strategies';

export function createBlock(type: string, id: string): StrategyBlock {
  if (
    [
      'OnStart',
      'OnResume',
      'OnCandleClosed',
      'OnOrderFilled',
      'OnOrderCancelled',
      'OnOrderRejected',
    ].includes(type)
  ) {
    return { id, kind: 'event', source: type };
  }
  if (type === 'condition') {
    return { id, kind: 'condition', join: 'all', checks: [] };
  }
  if (type === 'cancel') {
    return { id, kind: 'cancel' };
  }
  if (['MarketBuy', 'MarketSell', 'LimitBuy', 'LimitSell'].includes(type)) {
    return {
      id,
      kind: 'order',
      orderKind: type.startsWith('Limit') ? 'Limit' : 'Market',
      side: type.endsWith('Sell') ? 'Sell' : 'Buy',
    };
  }
  throw new Error('Неизвестный вид блока');
}
