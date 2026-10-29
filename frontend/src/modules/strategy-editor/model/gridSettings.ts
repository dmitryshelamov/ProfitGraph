import type { ValueExpression } from '@/modules/strategies';

/** One setting of a grid block (ADR 0058): its key, how the inspector names it, its value type and a new block's value. */
export interface GridSetting {
  readonly key: string;
  readonly label: string;
  readonly hint: string;
  readonly valueType: string;
  readonly initial: string;
}

export const gridSettings: readonly GridSetting[] = [
  {
    key: 'levels',
    label: 'Ступеней',
    hint: 'сколько покупок ниже цены старта',
    valueType: 'Integer',
    initial: '10',
  },
  {
    key: 'step',
    label: 'Шаг ступени',
    hint: 'доля между соседними ступенями',
    valueType: 'Scalar',
    initial: '0.01',
  },
  {
    key: 'profitStep',
    label: 'Шаг прибыли',
    hint: 'продажа ступени выше её покупки',
    valueType: 'Scalar',
    initial: '0.01',
  },
  {
    key: 'lot',
    label: 'Лот ступени',
    hint: 'сумма одной покупки в котировке',
    valueType: 'QuoteAmount',
    initial: '100',
  },
  {
    key: 'maxOpenBuys',
    label: 'Покупок в рынке',
    hint: 'не больше стольких заявок на покупку',
    valueType: 'Integer',
    initial: '10',
  },
  {
    key: 'shiftThreshold',
    label: 'Сдвиг вверх',
    hint: 'рост над ценой старта, 0 — не сдвигать',
    valueType: 'Scalar',
    initial: '0',
  },
  {
    key: 'anchorCeiling',
    label: 'Потолок сдвига',
    hint: 'насколько выше первой цены можно сдвинуться',
    valueType: 'Scalar',
    initial: '0.5',
  },
  {
    key: 'stopBelow',
    label: 'Стоп ниже сетки',
    hint: 'падение под нижнюю ступень, 0 — без стопа',
    valueType: 'Scalar',
    initial: '0',
  },
];

/** The settings of a new grid block: constants the user then edits or replaces with parameters. */
export function initialGridSettings(): Record<string, ValueExpression> {
  return Object.fromEntries(
    gridSettings.map((setting) => [
      setting.key,
      { kind: 'constant', valueType: setting.valueType, number: setting.initial },
    ]),
  );
}
