import { describe, expect, it } from 'vitest';

import type { FlowchartNode } from '../model/blockPresentation';
import { wireLook } from './wireLook';

const node = (patch: Partial<FlowchartNode>): FlowchartNode => ({
  id: 'b',
  kind: 'action',
  typeKey: 'order',
  title: 'Покупка',
  lines: [],
  details: [],
  outputs: [],
  detached: false,
  ...patch,
});

describe('wireLook', () => {
  it('draws an event wire solid in the event colour, named after the event', () => {
    expect(
      wireLook(node({ kind: 'start', typeKey: 'candleClosed', title: 'Закрытие свечи' }), 'out'),
    ).toEqual({
      color: '#8a96ab',
      dashed: false,
      label: 'Закрытие свечи',
    });
  });

  it('draws yes green and no red', () => {
    const decision = node({ kind: 'decision', typeKey: 'condition', title: 'Если' });
    expect(wireLook(decision, 'yes')).toEqual({ color: '#3cc184', dashed: false, label: 'да' });
    expect(wireLook(decision, 'no')).toEqual({ color: '#ef5350', dashed: false, label: 'нет' });
  });

  it('dashes order and cancel statuses in the block colour', () => {
    expect(wireLook(node({ side: 'Sell' }), 'filled')).toEqual({
      color: '#f07a5c',
      dashed: true,
      label: 'исполнен',
    });
    expect(wireLook(node({ typeKey: 'cancel', title: 'Отмена ордера' }), 'rejected')).toMatchObject(
      {
        color: '#d968a3',
        dashed: true,
      },
    );
  });
});
