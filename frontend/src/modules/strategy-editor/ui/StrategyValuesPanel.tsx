import { useState } from 'react';

import type { BlockCatalog, StrategyDefinition, StrategyDiagnostic } from '@/modules/strategies';
import { isPercentageParameter, parameterDisplayUnit } from '@/modules/strategies';
import { fractionToPercent, isBackendDecimal, percentToFraction } from '@/shared/lib/decimal';

import {
  removeParameter,
  removeValue,
  renameParameter,
  renameValue,
  upsertParameter,
  upsertValue,
} from '../model/editorCommands';
import { useEditorStore } from '../model/editorStore';
import { createNamedSettingDeletionLookup } from '../model/namedSettingDependencies';
import {
  expressionControl,
  expressionTypeLabel,
  newExpression,
  ValueExpressionEditor,
} from './ValueExpressionEditor';

const numericTypes = ['Integer', 'Scalar', 'Price', 'BaseAmount', 'QuoteAmount'];

export function StrategyValuesPanel({
  definition,
  catalog,
  diagnostics,
}: {
  definition: StrategyDefinition;
  catalog: BlockCatalog;
  diagnostics: readonly StrategyDiagnostic[];
}) {
  const editor = useEditorStore();
  const [name, setName] = useState('');
  const [kind, setKind] = useState<'parameter' | 'value'>('parameter');
  const [error, setError] = useState('');
  const execute = (command: Parameters<typeof editor.execute>[0]) => {
    try {
      editor.execute(command);
      setError('');
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Не удалось изменить значение.');
    }
  };
  const rename = (kind: 'parameter' | 'value', current: string) => (
    <form
      className="flex gap-2"
      onSubmit={(event) => {
        event.preventDefault();
        const next = new FormData(event.currentTarget).get('name')?.toString() ?? '';
        execute(kind === 'parameter' ? renameParameter(current, next) : renameValue(current, next));
      }}
    >
      <input
        name="name"
        aria-label={`Новое имя ${current}`}
        defaultValue={current}
        className={expressionControl}
      />
      <button type="submit" className="rounded border border-line px-2 text-xs">
        Переименовать
      </button>
    </form>
  );
  const deletionMessage = createNamedSettingDeletionLookup(definition);
  const add = () => {
    const next = name.trim();
    if (
      !next ||
      definition.parameters.some((parameter) => parameter.name === next) ||
      definition.values.some((value) => value.name === next)
    ) {
      setError('Введите уникальное имя параметра или значения.');
      return;
    }
    editor.execute(
      kind === 'parameter'
        ? upsertParameter({ name: next, valueType: 'Scalar', defaultValue: '0' })
        : upsertValue({ name: next, expression: newExpression('constant', definition) }),
    );
    setName('');
    setError('');
  };
  return (
    <section aria-label="Параметры и значения" className="space-y-4 p-4 text-sm">
      <h2 className="font-semibold">Параметры и значения</h2>
      <p className="text-xs text-muted">
        Именованные значения доступны в настройках любого блока. Параметры используются в
        оптимизации.
      </p>
      {definition.parameters.map((parameter) => (
        <fieldset
          key={parameter.name}
          aria-label={`Параметр ${parameter.name}`}
          className="space-y-2 rounded border border-line p-3"
        >
          <legend className="px-1">{parameter.name}</legend>
          {rename('parameter', parameter.name)}
          <label className="block text-xs text-muted">
            Тип
            <select
              className={expressionControl}
              aria-label={`Тип параметра ${parameter.name}`}
              value={parameter.valueType}
              onChange={(event) =>
                editor.execute(upsertParameter({ ...parameter, valueType: event.target.value }))
              }
            >
              {numericTypes.map((type) => (
                <option key={type} value={type}>
                  {expressionTypeLabel(type)}
                </option>
              ))}
            </select>
          </label>
          <label className="block text-xs text-muted">
            Единица отображения
            <select
              className={expressionControl}
              aria-label={`Единица параметра ${parameter.name}`}
              value={parameterDisplayUnit(parameter.displayUnit)}
              onChange={(event) =>
                editor.execute(upsertParameter({ ...parameter, displayUnit: event.target.value }))
              }
            >
              <option value="none">Без единицы</option>
              <option value="percent">Проценты (%)</option>
              <option value="quote">Валюта котировки</option>
              <option value="base">Базовый актив</option>
            </select>
          </label>
          <label className="block text-xs text-muted">
            Значение по умолчанию
            {isPercentageParameter(parameter.displayUnit) ? ', %' : ''}
            <ParameterDefaultInput
              key={parameterDisplayUnit(parameter.displayUnit)}
              name={parameter.name}
              value={parameter.defaultValue}
              percentage={isPercentageParameter(parameter.displayUnit)}
              onChange={(value) =>
                editor.execute(
                  upsertParameter(
                    { ...parameter, defaultValue: value },
                    `parameter:${parameter.name}`,
                  ),
                )
              }
              onBlur={editor.endHistoryGroup}
            />
          </label>
          {!isBackendDecimal(parameter.defaultValue) && (
            <p role="alert" className="text-xs text-danger-text">
              Введите точное число с точкой.
            </p>
          )}
          <button
            type="button"
            aria-label={`Удалить параметр ${parameter.name}`}
            disabled={deletionMessage('parameter', parameter.name) !== null}
            className="text-xs text-muted hover:text-danger-text"
            onClick={() => editor.execute(removeParameter(parameter.name))}
          >
            Удалить параметр
          </button>
          {deletionMessage('parameter', parameter.name) && (
            <p className="text-xs text-muted">{deletionMessage('parameter', parameter.name)}</p>
          )}
          {diagnostics
            .filter((item) => item.parameterName === parameter.name)
            .map((item, index) => (
              <p key={index} role="alert" className="text-xs text-danger-text">
                {item.message}
              </p>
            ))}
        </fieldset>
      ))}
      {definition.values.map((value) => (
        <fieldset
          key={value.name}
          aria-label={`Именованное значение ${value.name}`}
          className="space-y-2 rounded border border-line p-3"
        >
          <legend className="px-1">{value.name}</legend>
          {rename('value', value.name)}
          <ValueExpressionEditor
            label={value.name}
            expression={value.expression}
            definition={definition}
            catalog={catalog}
            excludeValue={value.name}
            onBlur={editor.endHistoryGroup}
            onChange={(expression) =>
              execute(upsertValue({ ...value, expression }, `value:${value.name}`))
            }
          />
          <button
            type="button"
            aria-label={`Удалить значение ${value.name}`}
            disabled={deletionMessage('value', value.name) !== null}
            className="text-xs text-muted hover:text-danger-text"
            onClick={() => editor.execute(removeValue(value.name))}
          >
            Удалить значение
          </button>
          {deletionMessage('value', value.name) && (
            <p className="text-xs text-muted">{deletionMessage('value', value.name)}</p>
          )}
          {diagnostics
            .filter((item) => item.valueName === value.name)
            .map((item, index) => (
              <p key={index} role="alert" className="text-xs text-danger-text">
                {item.message}
              </p>
            ))}
        </fieldset>
      ))}
      <form
        className="space-y-2 border-t border-line pt-4"
        onSubmit={(event) => {
          event.preventDefault();
          add();
        }}
      >
        <label className="block text-xs text-muted">
          Добавить
          <select
            className={expressionControl}
            aria-label="Тип именованного значения"
            value={kind}
            onChange={(event) => setKind(event.target.value as 'parameter' | 'value')}
          >
            <option value="parameter">Параметр</option>
            <option value="value">Значение</option>
          </select>
        </label>
        <label className="block text-xs text-muted">
          Имя
          <input
            className={expressionControl}
            aria-label="Имя нового значения"
            value={name}
            onChange={(event) => setName(event.target.value)}
          />
        </label>
        <button type="submit" className="rounded border border-line px-3 py-1">
          Добавить значение
        </button>
        {error && (
          <p role="alert" className="text-xs text-danger-text">
            {error}
          </p>
        )}
      </form>
    </section>
  );
}

function ParameterDefaultInput({
  name,
  value,
  percentage,
  onChange,
  onBlur,
}: {
  name: string;
  value: string;
  percentage: boolean;
  onChange: (value: string) => void;
  onBlur: () => void;
}) {
  const [draft, setDraft] = useState<{ stored: string; text: string } | null>(null);
  const displayed =
    draft?.stored === value
      ? draft.text
      : percentage && value.endsWith('%')
        ? value.slice(0, -1)
        : percentage
          ? (fractionToPercent(value) ?? value)
          : value;
  return (
    <input
      className={expressionControl}
      aria-label={`Значение параметра ${name}${percentage ? ', %' : ''}`}
      value={displayed}
      onChange={(event) => {
        const text = event.target.value;
        // Decimal drafts must never become unconverted fractions in a saved definition.
        const normalized = text
          .trim()
          .replace(/^(-?)0+(?=\d)/, '$1')
          .replace(/^(-?)\./, '$10.')
          .replace(/\.$/, '');
        const stored = percentage ? (percentToFraction(normalized) ?? `${text}%`) : text;
        setDraft({ stored, text });
        onChange(stored);
      }}
      onBlur={() => {
        setDraft(null);
        onBlur();
      }}
    />
  );
}
