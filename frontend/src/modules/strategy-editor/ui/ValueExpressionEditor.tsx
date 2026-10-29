import type { BlockCatalog, StrategyDefinition, ValueExpression } from '@/modules/strategies';
import { parameterValueDisplay } from '@/modules/strategies';

import { wouldCreateValueCycle } from '../model/namedSettingEdits';

export const expressionControl =
  'mt-1 w-full rounded border border-line bg-canvas px-2 py-1 text-sm';
const types: Record<string, string> = {
  Integer: 'Целое число',
  Scalar: 'Число / доля',
  Price: 'Цена',
  BaseAmount: 'Количество актива',
  QuoteAmount: 'Сумма',
  Tag: 'Метка',
  Boolean: 'Да / нет',
  OrderId: 'Идентификатор ордера',
  RejectionReason: 'Причина отклонения',
};
export const expressionTypeLabel = (type: string) => types[type] ?? type;
/** Value sources computed from the closed-candle history; their operands are edited as nested expressions. */
const indicatorKinds = ['SMA', 'ATR', 'EMA', 'RSI', 'Bollinger', 'MACD'];

export function newExpression(
  kind: string,
  definition: StrategyDefinition,
  valueType = 'Scalar',
): ValueExpression {
  switch (kind) {
    case 'parameter':
      return { kind, name: definition.parameters[0]?.name ?? '' };
    case 'value':
      return { kind, name: definition.values[0]?.name ?? '' };
    case 'candle':
      return { kind, field: 'Close' };
    case 'balance':
      return { kind, field: valueType === 'BaseAmount' ? 'baseAvailable' : 'quoteAvailable' };
    case 'position':
      return { kind, field: 'quantity' };
    case 'activeOrder':
      return {
        kind,
        field: 'price',
        arguments: { tag: { kind: 'constant', valueType: 'Tag', text: '' } },
      };
    case 'SMA':
      return {
        kind,
        arguments: {
          in: { kind: 'candle', field: 'Close' },
          period: { kind: 'constant', valueType: 'Integer', number: '20' },
        },
      };
    case 'ATR':
      return {
        kind,
        arguments: { period: { kind: 'constant', valueType: 'Integer', number: '14' } },
      };
    case 'EMA':
    case 'RSI':
      return {
        kind,
        arguments: {
          in: { kind: 'candle', field: 'Close' },
          period: { kind: 'constant', valueType: 'Integer', number: kind === 'EMA' ? '20' : '14' },
        },
      };
    case 'Bollinger':
      return {
        kind,
        field: 'upper',
        arguments: {
          in: { kind: 'candle', field: 'Close' },
          period: { kind: 'constant', valueType: 'Integer', number: '20' },
          multiplier: { kind: 'constant', valueType: 'Scalar', number: '2' },
        },
      };
    case 'MACD':
      return {
        kind,
        field: 'macd',
        arguments: {
          in: { kind: 'candle', field: 'Close' },
          fastPeriod: { kind: 'constant', valueType: 'Integer', number: '12' },
          slowPeriod: { kind: 'constant', valueType: 'Integer', number: '26' },
          signalPeriod: { kind: 'constant', valueType: 'Integer', number: '9' },
        },
      };
    case 'previous':
      return { kind, arguments: { in: { kind: 'candle', field: 'Close' } } };
    case 'arithmetic':
      return {
        kind,
        operation: 'Add',
        arguments: {
          a: newExpression('constant', definition, valueType),
          b: newExpression('constant', definition, valueType),
        },
      };
    default:
      if (valueType === 'OrderId') {
        return { kind: 'constant', valueType, number: '1' };
      }
      return ['Tag', 'Boolean', 'RejectionReason'].includes(valueType)
        ? {
            kind: 'constant',
            valueType,
            text:
              valueType === 'Boolean'
                ? 'false'
                : valueType === 'RejectionReason'
                  ? 'ZeroQuantity'
                  : '',
          }
        : { kind: 'constant', valueType, number: '0' };
  }
}

export function ValueExpressionEditor({
  label,
  expression,
  definition,
  catalog,
  valueType = 'Scalar',
  onChange,
  onBlur,
  excludeValue,
}: {
  label: string;
  expression: ValueExpression | null | undefined;
  definition: StrategyDefinition;
  catalog: BlockCatalog;
  valueType?: string;
  onChange: (expression: ValueExpression) => void;
  onBlur?: () => void;
  excludeValue?: string;
}) {
  const current = expression ?? newExpression('constant', definition, valueType);
  const source = catalog.valueSources.find((entry) => entry.key === current.kind);
  const change = (patch: Partial<ValueExpression>) => onChange({ ...current, ...patch });
  const discrete = (next: ValueExpression) => {
    onBlur?.();
    onChange(next);
    onBlur?.();
  };
  const changeDiscrete = (patch: Partial<ValueExpression>) => discrete({ ...current, ...patch });
  const fields = source?.fields ?? [];
  const constants = catalog.valueSources.find((entry) => entry.key === 'constant')?.choices ?? [];
  return (
    <fieldset className="min-w-0 space-y-2 rounded border border-line p-2" aria-label={label}>
      <legend className="px-1 text-xs text-muted">{label}</legend>
      <label className="block text-xs text-muted">
        Источник
        <select
          className={expressionControl}
          aria-label={`Источник ${label}`}
          value={expression?.kind ?? ''}
          onChange={(event) => {
            const next = newExpression(event.target.value, definition, valueType);
            if (next.kind === 'value' && excludeValue) {
              next.name =
                definition.values.find(
                  (value) =>
                    !wouldCreateValueCycle(definition, excludeValue, {
                      kind: 'value',
                      name: value.name,
                    }),
                )?.name ?? '';
            }
            discrete(next);
          }}
        >
          <option value="">Выберите источник</option>
          {catalog.valueSources.map((entry) => (
            <option key={entry.key} value={entry.key}>
              {entry.label}
            </option>
          ))}
        </select>
      </label>
      {expression && (
        <>
          {(current.kind === 'parameter' || current.kind === 'value') && (
            <label className="block text-xs text-muted">
              {current.kind === 'parameter' ? 'Параметр' : 'Именованное значение'}
              <select
                className={expressionControl}
                aria-label={`Ссылка ${label}`}
                value={current.name ?? ''}
                onChange={(event) => changeDiscrete({ name: event.target.value })}
              >
                <option value="">Выберите значение</option>
                {(current.kind === 'parameter'
                  ? definition.parameters
                  : definition.values.filter(
                      (value) =>
                        !excludeValue ||
                        !wouldCreateValueCycle(definition, excludeValue, {
                          kind: 'value',
                          name: value.name,
                        }),
                    )
                ).map((value) => (
                  <option key={value.name} value={value.name}>
                    {value.name}
                  </option>
                ))}
              </select>
            </label>
          )}
          {current.kind === 'parameter' &&
            (() => {
              const parameter = definition.parameters.find((item) => item.name === current.name);
              return parameter ? (
                <p className="text-xs text-muted">
                  По умолчанию:{' '}
                  <span title={parameter.defaultValue}>
                    {parameterValueDisplay(parameter.defaultValue, parameter.displayUnit)}
                  </span>
                </p>
              ) : null;
            })()}
          {current.kind === 'constant' && (
            <>
              <label className="block text-xs text-muted">
                Тип значения
                <select
                  className={expressionControl}
                  aria-label={`Тип ${label}`}
                  value={current.valueType ?? valueType}
                  onChange={(event) =>
                    discrete(newExpression('constant', definition, event.target.value))
                  }
                >
                  {constants.map((type) => (
                    <option key={type} value={type}>
                      {expressionTypeLabel(type)}
                    </option>
                  ))}
                </select>
              </label>
              <label className="block text-xs text-muted">
                Значение
                <input
                  className={expressionControl}
                  aria-label={`Значение ${label}`}
                  value={current.number ?? current.text ?? ''}
                  onBlur={onBlur}
                  onChange={(event) =>
                    change(
                      current.number != null
                        ? { number: event.target.value }
                        : { text: event.target.value },
                    )
                  }
                />
              </label>
            </>
          )}
          {fields.length > 0 && (
            <label className="block text-xs text-muted">
              Поле
              <select
                className={expressionControl}
                aria-label={`Поле ${label}`}
                value={current.field ?? fields[0]?.key ?? ''}
                onChange={(event) => changeDiscrete({ field: event.target.value })}
              >
                {fields.map((field) => (
                  <option key={field.key} value={field.key}>
                    {field.label}
                  </option>
                ))}
              </select>
            </label>
          )}
          {current.kind === 'activeOrder' && (
            <ValueExpressionEditor
              label={`${label}: Метка ордера`}
              expression={
                current.arguments?.tag ?? {
                  kind: 'constant',
                  valueType: 'Tag',
                  text: current.text ?? '',
                }
              }
              definition={definition}
              catalog={catalog}
              valueType="Tag"
              onBlur={onBlur}
              excludeValue={excludeValue}
              onChange={(tag) => change({ text: null, arguments: { ...current.arguments, tag } })}
            />
          )}
          {current.kind === 'arithmetic' && (
            <label className="block text-xs text-muted">
              Операция
              <select
                className={expressionControl}
                aria-label={`Операция ${label}`}
                value={current.operation ?? 'Add'}
                onChange={(event) => changeDiscrete({ operation: event.target.value })}
              >
                {(source?.choices ?? []).map((operation) => (
                  <option key={operation} value={operation}>
                    {(
                      {
                        Add: '+',
                        Subtract: '−',
                        Multiply: '×',
                        Divide: '÷',
                        Min: 'Минимум',
                        Max: 'Максимум',
                        Modulo: 'Остаток от деления',
                      } as Record<string, string>
                    )[operation] ?? operation}
                  </option>
                ))}
              </select>
            </label>
          )}
          {indicatorKinds.concat('previous', 'arithmetic').includes(current.kind) &&
            source?.operands.map((operand) => (
              <ValueExpressionEditor
                key={operand.key}
                label={`${label}: ${operand.label}`}
                expression={current.arguments?.[operand.key]}
                definition={definition}
                catalog={catalog}
                valueType={operand.valueTypes.length === 1 ? operand.valueTypes[0] : valueType}
                excludeValue={excludeValue}
                onBlur={onBlur}
                onChange={(next) =>
                  change({ arguments: { ...current.arguments, [operand.key]: next } })
                }
              />
            ))}
        </>
      )}
    </fieldset>
  );
}
