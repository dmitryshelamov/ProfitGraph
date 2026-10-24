import { Fragment, type ReactNode, useId, useState } from 'react';

import type {
  BlockCatalog,
  StrategyBlock,
  StrategyCheck,
  StrategyDefinition,
  StrategyDiagnostic,
  ValueExpression,
} from '@/modules/strategies';
import { strategyBlockLabel } from '@/modules/strategies';

import {
  blockInputs,
  blockTone,
  blockValues,
  checkTokens,
  exitTargets,
  formulaText,
  type FormulaToken,
  formulaTokens,
  valueUsers,
} from '../model/blockInsight';
import { eventExitLabel, expressionText } from '../model/blockPresentation';
import {
  addCheck,
  blockExits,
  removeCheck,
  reorderCheck,
  setConditionJoin,
  updateBlock,
  updateCheck,
  upsertValue,
} from '../model/editorCommands';
import { useEditorStore } from '../model/editorStore';
import {
  needsCandle,
  needsOrderConversion,
  type OrderSizeMode,
  orderSizeMode,
  previewOrder,
  showsOrderPrice,
  startsWithoutCandle,
  withMinimumOrderPolicy,
  withOrderKind,
  withOrderSide,
  withOrderSizeMode,
  withPreviewSettings,
} from '../model/orderSizeSettings';
import { newExpression, ValueExpressionEditor } from './ValueExpressionEditor';

const kindLines: Record<string, string> = {
  event: 'Событие · начало цепочки',
  condition: 'Условие · развилка да / нет',
  order: 'Ордер · выставить заявку',
  cancel: 'Отмена · снять заявку',
};
const eventHints: Record<string, string> = {
  OnStart: 'один раз, в самом начале прогона',
  OnResume: 'после перезапуска бота или смены графа; причина передаётся в событии',
  OnCandleClosed: 'на каждой свече, когда известна её цена закрытия',
  OnOrderFilled: 'когда ордер исполнился, можно выбрать метку',
  OnOrderCancelled: 'когда ордер сняли, можно выбрать метку',
  OnOrderRejected: 'когда ордер не приняли, можно выбрать метку',
};
const exitHints: Record<string, Record<string, string>> = {
  event: { out: '' },
  condition: { yes: 'проверки верны', no: 'проверки не верны' },
  order: {
    submitted: 'заявка принята',
    filled: 'сделка прошла',
    cancelled: 'заявку сняли',
    rejected: 'не приняли',
  },
  cancel: { completed: 'заявка снята', rejected: 'снять не удалось' },
};
const sizeNames = { amount: 'Сумма', quantity: 'Количество', price: 'Цена' } as const;
const sizeAccusative = { amount: 'сумму', quantity: 'количество', price: 'цену' } as const;
const minimumOrderPolicyLabels: Record<string, string> = {
  Reject: 'Отклонять ниже минимума',
  RaiseToExchangeMinimum: 'Увеличивать до минимума биржи',
};
const minimumOrderPolicyHint =
  'Для Bybit увеличивает сумму покупки до минимальной стоимости биржи. Для инструмента с целыми лотами может увеличить покупку до одного лота. Лимиты риска и доступный баланс сохраняются.';

function Formula({ tokens }: { tokens: readonly FormulaToken[] }) {
  // Spaces between the pieces keep the formula readable as text; the flex gap spaces it on screen.
  return (
    <>
      {tokens.map((token, index) => (
        <Fragment key={index}>
          {index > 0 && ' '}
          {token.kind === 'text' ? (
            <span>{token.text}</span>
          ) : (
            <span className={`insp-token is-${token.kind}`}>{token.text}</span>
          )}
        </Fragment>
      ))}
    </>
  );
}

function Step({
  index,
  title,
  name,
  last = false,
  children,
}: {
  index: number;
  title: string;
  name: string;
  last?: boolean;
  children: ReactNode;
}) {
  return (
    <section className="insp-step" aria-label={name}>
      <div className="insp-rail" aria-hidden="true">
        <span className={`insp-num${index === 2 ? ' is-accent' : ''}`}>{index}</span>
        {!last && <span className="insp-line" />}
      </div>
      <div className="insp-body">
        <h3 className="insp-title">{title}</h3>
        {children}
      </div>
    </section>
  );
}

function Choice({
  label,
  value,
  options,
  onChange,
}: {
  label: string;
  value: string;
  options: readonly { value: string; label: string }[];
  onChange: (value: string) => void;
}) {
  const name = useId();
  return (
    <div role="radiogroup" aria-label={label} className="insp-seg">
      {options.map((option) => (
        <label key={option.value} className={option.value === value ? 'is-on' : undefined}>
          <input
            type="radio"
            className="sr-only"
            name={name}
            value={option.value}
            checked={option.value === value}
            onChange={() => onChange(option.value)}
          />
          {option.label}
        </label>
      ))}
    </div>
  );
}

export function FlowchartInspector({
  block,
  definition,
  catalog,
  diagnostics,
  fieldErrors,
  onClose,
}: {
  block: StrategyBlock;
  definition: StrategyDefinition;
  catalog: BlockCatalog;
  diagnostics: readonly StrategyDiagnostic[];
  fieldErrors: Readonly<Record<string, readonly string[]>>;
  onClose?: () => void;
}) {
  const editor = useEditorStore();
  const orderEntry = catalog.orderKinds.find((entry) => entry.key === block.orderKind);
  const [openCheck, setOpenCheck] = useState<{ block: string; index: number } | null>(null);
  const [openValue, setOpenValue] = useState<{ block: string; name: string } | null>(null);
  const [openFields, setOpenFields] = useState<readonly string[]>([]);
  const [adding, setAdding] = useState(false);
  const [error, setError] = useState<string>();
  const blocks = new Map(definition.blocks.map((item) => [item.id, item]));
  const label = (item: StrategyBlock) => strategyBlockLabel(item, definition, catalog);
  const title = label(block);
  const twins = definition.blocks.filter(
    (item) => item.id !== block.id && label(item) === title,
  ).length;
  const run = (make: () => void) => {
    try {
      make();
      setError(undefined);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Не удалось изменить блок');
    }
  };
  const change = (patch: Partial<StrategyBlock>, group?: string) =>
    editor.execute(updateBlock({ ...block, ...patch }, group));
  const expression = (name: string, key: 'tag' | 'amount' | 'quantity' | 'price', type: string) => (
    <ValueExpressionEditor
      label={name}
      expression={block[key]}
      definition={definition}
      catalog={catalog}
      valueType={type}
      onChange={(value) => change({ [key]: value }, `${block.id}:${key}`)}
      onBlur={editor.endHistoryGroup}
    />
  );
  // A setting shows its value as a short formula; its full editor opens on demand or when it is empty.
  const field = (name: string, key: 'tag' | 'amount' | 'quantity' | 'price', type: string) => {
    const id = `${block.id}:${key === 'amount' || key === 'quantity' ? 'size' : key}`;
    const empty = !block[key];
    const open = empty || openFields.includes(id);
    return {
      inline: (
        <>
          {empty ? (
            <span className="insp-muted">не задано</span>
          ) : (
            <Formula tokens={formulaTokens(block[key], definition, catalog)} />
          )}
          {!empty && (
            <button
              type="button"
              className="insp-link insp-push"
              aria-expanded={open}
              aria-label={`${open ? 'Свернуть' : 'Изменить'}: ${name}`}
              onClick={() =>
                setOpenFields((current) =>
                  open ? current.filter((item) => item !== id) : [...current, id],
                )
              }
            >
              {open ? 'Готово' : 'Изменить'}
            </button>
          )}
        </>
      ),
      editor: open && <div className="insp-edit is-field">{expression(name, key, type)}</div>,
    };
  };
  const blockChip = (id: string, suffix?: string) => {
    const item = blocks.get(id);
    if (!item) {
      return null;
    }
    return (
      <button
        key={`${id}:${suffix ?? ''}`}
        type="button"
        className="insp-chip"
        title="Открыть блок"
        onClick={() => editor.select(id)}
      >
        <span className={`insp-sq tone-${blockTone(item)}`} aria-hidden="true" />
        {label(item)}
        {suffix && <span className="insp-muted"> · {suffix}</span>}
      </button>
    );
  };
  const editCheck = (index: number, check: StrategyCheck, group?: string) =>
    editor.execute(updateCheck(block.id, index, check, group));
  const isOpen = (index: number) => openCheck?.block === block.id && openCheck.index === index;
  const newCheck = (type: string): StrategyCheck => {
    const entry = catalog.checkTypes.find((item) => item.key === type);
    const operands: Record<string, ValueExpression> = {};
    for (const operand of entry?.operands ?? []) {
      if (operand.required && operand.key !== 'tag') {
        operands[operand.key] = newExpression('constant', definition, operand.valueTypes[0]);
      }
    }
    return {
      type,
      operands,
      ...(type === 'Compare' ? { operation: '>' } : {}),
      ...(type === 'NoActiveTaggedOrder'
        ? { tag: newExpression('constant', definition, 'Tag') }
        : {}),
      ...(type === 'OrderSizePreviewValid'
        ? {
            side: 'Buy',
            orderKind: 'Market',
            operands: { amount: newExpression('constant', definition, 'QuoteAmount') },
          }
        : {}),
    };
  };

  const inputs = blockInputs(definition, block.id);
  const values = blockValues(block, definition);
  const checks = block.checks ?? [];

  const inputStep = (
    <Step
      index={1}
      name="Вход"
      title={block.kind === 'event' ? 'Вход' : 'Вход · когда запускается'}
    >
      {block.kind === 'event' ? (
        <p className="insp-text">
          Ничего не входит. Блок запускает сам прогон, с него начинается цепочка.
        </p>
      ) : inputs.length === 0 ? (
        <p className="insp-text">
          Сюда пока ничего не подключено. Протяните провод от выхода другого блока к входу «когда».
        </p>
      ) : (
        <>
          <div className="insp-chips is-column">
            {inputs.map((wire) => blockChip(wire.from.block, eventExitLabel(wire.from.exit)))}
          </div>
          {inputs.length > 1 && <p className="insp-hint">Достаточно любого из них.</p>}
        </>
      )}
    </Step>
  );

  const valuesPart = values.length > 0 && (
    <div className="insp-group" aria-label="Считает" role="group">
      <h4 className="insp-sub">
        Считает <span className="insp-muted">именованные значения, которые нужны блоку</span>
      </h4>
      {values.map((name) => {
        const value = definition.values.find((item) => item.name === name)!;
        const open = openValue?.block === block.id && openValue.name === name;
        const users = valueUsers(definition, name, block.id);
        return (
          <div key={name} className={`insp-card${open ? ' is-open' : ''}`}>
            <div className="insp-formula">
              <span className="insp-token is-value">{name}</span>
              <span className="insp-arrow">=</span>
              <Formula tokens={formulaTokens(value.expression, definition, catalog)} />
              <button
                type="button"
                className="insp-link insp-push"
                aria-label={`${open ? 'Свернуть' : 'Изменить'} значение ${name}`}
                aria-expanded={open}
                onClick={() => setOpenValue(open ? null : { block: block.id, name })}
              >
                {open ? 'Готово' : 'Изменить'}
              </button>
            </div>
            {open && (
              <div className="insp-edit">
                <ValueExpressionEditor
                  label={name}
                  expression={value.expression}
                  definition={definition}
                  catalog={catalog}
                  excludeValue={name}
                  onBlur={editor.endHistoryGroup}
                  onChange={(next) =>
                    run(() =>
                      editor.execute(upsertValue({ ...value, expression: next }, `value:${name}`)),
                    )
                  }
                />
                <p className="insp-hint">
                  {users.length > 0
                    ? `Значение общее: изменится и в ${users.length === 1 ? 'блоке' : 'блоках'} ${users.map((item) => `«${label(item)}»`).join(', ')}.`
                    : 'Значение используется только в этом блоке.'}
                </p>
              </div>
            )}
          </div>
        );
      })}
    </div>
  );

  const checkEditor = (check: StrategyCheck, index: number) => {
    const entry = catalog.checkTypes.find((item) => item.key === check.type);
    const patch = (value: Partial<StrategyCheck>, group?: string) =>
      editCheck(index, { ...check, ...value }, group);
    return (
      <div className="insp-edit">
        <div className="insp-edit-head">
          <span className="insp-edit-title">
            Проверка {index + 1} · {entry?.label ?? check.type}
          </span>
          <button
            type="button"
            className="insp-icon"
            disabled={index === 0}
            aria-label={`Поднять проверку ${index + 1}`}
            onClick={() => {
              editor.execute(reorderCheck(block.id, index, index - 1));
              setOpenCheck({ block: block.id, index: index - 1 });
            }}
          >
            ↑
          </button>
          <button
            type="button"
            className="insp-icon"
            disabled={index === checks.length - 1}
            aria-label={`Опустить проверку ${index + 1}`}
            onClick={() => {
              editor.execute(reorderCheck(block.id, index, index + 1));
              setOpenCheck({ block: block.id, index: index + 1 });
            }}
          >
            ↓
          </button>
          <button
            type="button"
            className="insp-link is-danger"
            aria-label={`Удалить проверку ${index + 1}`}
            onClick={() => {
              editor.execute(removeCheck(block.id, index));
              setOpenCheck(null);
            }}
          >
            Удалить
          </button>
        </div>
        {check.type === 'Compare' && (
          <Choice
            label={`Операция проверки ${index + 1}`}
            value={check.operation ?? '>'}
            options={(entry?.choices ?? []).map((operation) => ({
              value: operation,
              label: { '<=': '≤', '>=': '≥', '!=': '≠' }[operation] ?? operation,
            }))}
            onChange={(operation) => patch({ operation })}
          />
        )}
        {(check.type === 'NoActiveTaggedOrder' || check.type === 'PositionFlatPreview') && (
          <Choice
            label={`Результат проверки ${index + 1}`}
            value={check.operation ?? 'out'}
            options={
              check.type === 'NoActiveTaggedOrder'
                ? [
                    { value: 'out', label: 'Ордера нет' },
                    { value: 'exists', label: 'Ордер есть' },
                  ]
                : [
                    { value: 'out', label: 'Остаток нельзя продать' },
                    { value: 'valid', label: 'Остаток можно продать' },
                  ]
            }
            onChange={(operation) => patch({ operation })}
          />
        )}
        {check.type === 'OrderSizePreviewValid' && (
          <div className="insp-fields">
            <Choice
              label={`Сторона проверки ${index + 1}`}
              value={check.side ?? 'Buy'}
              options={(entry?.sides ?? ['Buy', 'Sell']).map((side) => ({
                value: side,
                label: side === 'Buy' ? 'Купить' : 'Продать',
              }))}
              onChange={(side) =>
                editCheck(
                  index,
                  withPreviewSettings(check, {
                    side,
                    sizeMode: orderSizeMode(previewOrder(check)),
                  }),
                )
              }
            />
            <Choice
              label={`Тип ордера проверки ${index + 1}`}
              value={check.orderKind ?? 'Market'}
              options={catalog.orderKinds
                .filter((kind) => !entry?.orderKinds || entry.orderKinds.includes(kind.key))
                .map((kind) => ({ value: kind.key, label: kind.label }))}
              onChange={(orderKind) => editCheck(index, withPreviewSettings(check, { orderKind }))}
            />
            <Choice
              label={`Размер проверки ${index + 1}`}
              value={orderSizeMode(previewOrder(check))}
              options={(entry?.sizeModes ?? ['amount', 'quantity']).map((mode) => ({
                value: mode,
                label: mode === 'amount' ? 'На сумму' : 'Количество',
              }))}
              onChange={(sizeMode) => editCheck(index, withPreviewSettings(check, { sizeMode }))}
            />
            {check.side !== 'Sell' && entry?.minimumOrderPolicies?.length ? (
              <>
                <Choice
                  label={`Минимальный размер проверки ${index + 1}`}
                  value={check.minimumOrderPolicy ?? 'Reject'}
                  options={entry.minimumOrderPolicies.map((policy) => ({
                    value: policy,
                    label: minimumOrderPolicyLabels[policy] ?? policy,
                  }))}
                  onChange={(minimumOrderPolicy) =>
                    editCheck(index, withPreviewSettings(check, { minimumOrderPolicy }))
                  }
                />
                <p className="insp-hint">{minimumOrderPolicyHint}</p>
              </>
            ) : null}
          </div>
        )}
        {check.type === 'OrderSizePreviewValid' && needsOrderConversion(previewOrder(check)) && (
          <p className="insp-hint">
            Размер проверки пересчитывается по цене сигнала. При исполнении и округлении фактический
            размер может измениться.
          </p>
        )}
        {check.type === 'OrderSizePreviewValid' &&
          check.orderKind !== 'Limit' &&
          needsOrderConversion(previewOrder(check)) &&
          startsWithoutCandle(definition, block.id) &&
          needsCandle(check.operands?.price, definition) && (
            <p role="alert" className="insp-warn">
              От события «Старт» свеча для цены пересчёта проверки ещё недоступна. Выберите
              константу или доступное значение.
            </p>
          )}
        {entry?.operands
          .filter(
            (operand) =>
              check.type !== 'OrderSizePreviewValid' ||
              operand.key === orderSizeMode(previewOrder(check)) ||
              (operand.key === 'quoteLimit' && check.side !== 'Sell') ||
              (operand.key === 'price' && showsOrderPrice(previewOrder(check))),
          )
          .map((operand) => (
            <ValueExpressionEditor
              key={operand.key}
              label={`Проверка ${index + 1}: ${operand.key === 'price' && check.orderKind !== 'Limit' && needsOrderConversion(previewOrder(check)) ? 'Цена пересчёта' : operand.label}`}
              expression={operand.key === 'tag' ? check.tag : check.operands?.[operand.key]}
              definition={definition}
              catalog={catalog}
              valueType={operand.valueTypes[0]}
              onChange={(value) =>
                patch(
                  operand.key === 'tag'
                    ? { tag: value }
                    : { operands: { ...check.operands, [operand.key]: value } },
                  `${block.id}:check:${index}:${operand.key}`,
                )
              }
              onBlur={editor.endHistoryGroup}
            />
          ))}
        {check.type === 'OrderSizePreviewValid' && (
          <section
            aria-label={`Сохранённые настройки проверки ${index + 1}`}
            className="insp-saved"
          >
            {(['amount', 'quantity', 'price'] as const)
              .filter(
                (key) =>
                  check.operands?.[key] &&
                  (key === 'price'
                    ? !showsOrderPrice(previewOrder(check))
                    : key !== orderSizeMode(previewOrder(check))),
              )
              .map((key) => (
                <div key={key}>
                  <p className="insp-hint">
                    Сохранено для другого режима, не исполняется. Ссылки удерживают параметр или
                    значение до очистки.
                  </p>
                  <ValueExpressionEditor
                    label={`Проверка ${index + 1}: Сохранённое ${sizeNames[key]}`}
                    expression={check.operands?.[key]}
                    definition={definition}
                    catalog={catalog}
                    onChange={(value) => patch({ operands: { ...check.operands, [key]: value } })}
                  />
                  <button
                    type="button"
                    className="insp-link"
                    onClick={() => {
                      const operands = { ...check.operands };
                      delete operands[key];
                      patch({ operands });
                    }}
                  >
                    Очистить сохранённое {sizeAccusative[key]} проверки {index + 1}
                  </button>
                </div>
              ))}
          </section>
        )}
        <button type="button" className="insp-button" onClick={() => setOpenCheck(null)}>
          Готово
        </button>
      </div>
    );
  };

  const conditionPart = block.kind === 'condition' && (
    <div className="insp-group" role="group" aria-label="Проверяет">
      <h4 className="insp-sub">
        Проверяет
        <label className="insp-muted insp-join">
          ответ «да»,
          <select
            className="insp-select"
            aria-label="Объединение условий"
            value={block.join ?? 'all'}
            onChange={(event) =>
              editor.execute(setConditionJoin(block.id, event.target.value as 'all' | 'any'))
            }
          >
            <option value="all">когда верны все</option>
            <option value="any">когда верна любая</option>
          </select>
        </label>
      </h4>
      {checks.length === 0 && (
        <p className="insp-text">Проверок пока нет. Добавьте хотя бы одну.</p>
      )}
      {checks.map((check, index) => {
        const open = isOpen(index);
        const problems = diagnostics.filter(
          (item) => item.blockId === block.id && item.checkIndex === index,
        );
        return (
          <fieldset
            key={index}
            aria-label={`Проверка ${index + 1}`}
            className={`insp-card insp-check${open ? ' is-open' : ''}${problems.length ? ' is-error border-danger-border' : ''}`}
          >
            {open ? (
              checkEditor(check, index)
            ) : (
              <div className="insp-formula">
                <span className="insp-index">{index + 1}</span>
                <Formula tokens={checkTokens(check, definition, catalog)} />
                <button
                  type="button"
                  className="insp-link insp-push"
                  aria-label={`Изменить проверку ${index + 1}`}
                  onClick={() => setOpenCheck({ block: block.id, index })}
                >
                  Изменить
                </button>
              </div>
            )}
            {problems.map((item, problem) => (
              <p key={problem} role="alert" className="insp-error">
                Проверка {index + 1}: {item.message}
              </p>
            ))}
          </fieldset>
        );
      })}
      <button
        type="button"
        className="insp-link"
        aria-expanded={adding}
        onClick={() => setAdding((current) => !current)}
      >
        + Проверка
      </button>
      {adding && (
        <div role="menu" aria-label="Новая проверка" className="insp-menu">
          {catalog.checkTypes.map((type) => (
            <button
              key={type.key}
              type="button"
              role="menuitem"
              onClick={() => {
                editor.execute(addCheck(block.id, newCheck(type.key)));
                setOpenCheck({ block: block.id, index: checks.length });
                setAdding(false);
              }}
            >
              {type.label}
            </button>
          ))}
        </div>
      )}
    </div>
  );

  const price = showsOrderPrice(block)
    ? field(block.orderKind === 'Limit' ? 'Цена' : 'Цена пересчёта', 'price', 'Price')
    : null;
  const size =
    orderSizeMode(block) === 'quantity'
      ? field('Количество', 'quantity', 'BaseAmount')
      : field('Сумма', 'amount', 'QuoteAmount');
  const tag = field('Метка', 'tag', 'Tag');
  const eventPart = block.kind === 'event' && (
    <>
      <div className="insp-group">
        <h4 className="insp-sub">Что ловит</h4>
        <div role="radiogroup" aria-label="Источник события" className="insp-options">
          {catalog.eventSources.map((source) => (
            <label
              key={source.key}
              className={`insp-option${block.source === source.key ? ' is-on' : ''}`}
            >
              <input
                type="radio"
                name={`event-source-${block.id}`}
                value={source.key}
                checked={block.source === source.key}
                onChange={() => {
                  const previousDefault = catalog.eventSources.find(
                    (entry) => entry.key === block.source,
                  )?.label;
                  change({
                    source: source.key,
                    ...(previousDefault && title === previousDefault
                      ? { displayName: source.label }
                      : {}),
                    tag: source.key.startsWith('OnOrder') ? block.tag : null,
                  });
                }}
              />
              <span>
                {source.label}
                {eventHints[source.key] && (
                  <span className="insp-hint is-block">{eventHints[source.key]}</span>
                )}
              </span>
            </label>
          ))}
        </div>
        {block.source?.startsWith('OnOrder') && (
          <div className="insp-fields">
            {block.tag ? (
              <>
                <div className="insp-row">
                  <span className="insp-label">Метка</span>
                  {tag.inline}
                </div>
                {tag.editor}
                <button type="button" className="insp-link" onClick={() => change({ tag: null })}>
                  Все метки
                </button>
              </>
            ) : (
              <p className="insp-hint">
                Все метки.{' '}
                <button
                  type="button"
                  className="insp-link"
                  onClick={() => change({ tag: newExpression('constant', definition, 'Tag') })}
                >
                  Фильтровать по метке
                </button>
              </p>
            )}
          </div>
        )}
      </div>
      <div className="insp-group">
        <h4 className="insp-sub">Что передаёт дальше</h4>
        {block.source === 'OnCandleClosed' && (
          <p className="insp-text">Свеча: открытие, максимум, минимум, закрытие и объём.</p>
        )}
        {block.source === 'OnStart' && (
          <p className="insp-text">Закрытой свечи ещё нет, поля свечи здесь недоступны.</p>
        )}
        {block.source === 'OnResume' && (
          <p className="insp-text">
            Событие содержит причину: перезапуск или смена графа. В бэктесте оно не возникает.
          </p>
        )}
        <p className="insp-hint">
          Баланс, позиция и ордера по метке доступны в любом блоке, их передавать не нужно.
        </p>
      </div>
    </>
  );

  const orderSummary = () => {
    const quantity = orderSizeMode(block) === 'quantity';
    const size = quantity
      ? block.quantity?.kind === 'balance' && block.quantity.field === 'baseAvailable'
        ? 'весь доступный актив'
        : formulaText(formulaTokens(block.quantity, definition, catalog))
      : `на ${formulaText(formulaTokens(block.amount, definition, catalog))}`;
    const price = formulaText(formulaTokens(block.price, definition, catalog));
    return (
      <div className="insp-summary">
        {block.side === 'Sell' ? 'Продать' : 'Купить'} <b>{size}</b>{' '}
        {block.orderKind === 'Limit' ? (
          <>
            лимитной заявкой по <b>{price}</b>.
            <span className="insp-hint is-block">
              Заявка ждёт, пока цена не дойдёт до {price} или её не отменят.
            </span>
          </>
        ) : (
          <>по рынку.</>
        )}
      </div>
    );
  };

  const orderPart = block.kind === 'order' && (
    <div className="insp-group" role="group" aria-label="Выставляет">
      <h4 className="insp-sub">Выставляет</h4>
      <div className="insp-row">
        <span className="insp-label">Действие</span>
        <Choice
          label="Сторона ордера"
          value={block.side ?? 'Buy'}
          options={(orderEntry?.sides ?? ['Buy', 'Sell']).map((side) => ({
            value: side,
            label: side === 'Buy' ? 'Купить' : 'Продать',
          }))}
          onChange={(side) => editor.execute(updateBlock(withOrderSide(block, side)))}
        />
      </div>
      <div className="insp-row">
        <span className="insp-label">Цена</span>
        <Choice
          label="Тип ордера"
          value={block.orderKind ?? 'Market'}
          options={catalog.orderKinds.map((kind) => ({
            value: kind.key,
            label:
              kind.key === 'Market' ? 'По рынку' : kind.key === 'Limit' ? 'Лимитная' : kind.label,
          }))}
          onChange={(orderKind) => editor.execute(updateBlock(withOrderKind(block, orderKind)))}
        />
      </div>
      {price && <div className="insp-row is-indent">{price.inline}</div>}
      {price?.editor}
      <div className="insp-row">
        <span className="insp-label">Размер</span>
        <Choice
          label="Размер ордера"
          value={orderSizeMode(block)}
          options={(orderEntry?.sizeModes ?? ['amount', 'quantity']).map((mode) => ({
            value: mode,
            label: mode === 'amount' ? 'На сумму' : 'Количество',
          }))}
          onChange={(mode) =>
            editor.execute(updateBlock(withOrderSizeMode(block, mode as OrderSizeMode)))
          }
        />
      </div>
      <div className="insp-row is-indent">{size.inline}</div>
      {size.editor}
      {block.side === 'Buy' && orderEntry?.minimumOrderPolicies?.length ? (
        <>
          <div className="insp-row">
            <span className="insp-label">Минимум биржи</span>
            <Choice
              label="Минимальный размер ордера"
              value={block.minimumOrderPolicy ?? 'Reject'}
              options={orderEntry.minimumOrderPolicies.map((policy) => ({
                value: policy,
                label: minimumOrderPolicyLabels[policy] ?? policy,
              }))}
              onChange={(policy) =>
                editor.execute(updateBlock(withMinimumOrderPolicy(block, policy)))
              }
            />
          </div>
          <p className="insp-hint">{minimumOrderPolicyHint}</p>
        </>
      ) : null}
      {block.orderKind === 'Limit' && needsOrderConversion(block) && (
        <p className="insp-hint">
          Размер пересчитывается по указанной лимитной цене в момент сигнала. После округления цены
          и размера фактическая сумма или количество могут отличаться. Комиссии применяются по
          обычным правилам прогона.
        </p>
      )}
      {block.orderKind !== 'Limit' && needsOrderConversion(block) && (
        <p className="insp-hint">
          Размер пересчитывается по указанной цене в момент сигнала. При рыночном исполнении
          фактическая сумма или количество могут измениться с ценой. Округление и комиссии
          применяются по обычным правилам прогона.
        </p>
      )}
      {block.orderKind !== 'Limit' &&
        needsOrderConversion(block) &&
        startsWithoutCandle(definition, block.id) &&
        needsCandle(block.price, definition) && (
          <p role="alert" className="insp-warn">
            От события «Старт» закрытая свеча ещё недоступна. Для цены пересчёта выберите константу
            или доступное значение, иначе ордер не будет выставлен.
          </p>
        )}
      <div className="insp-row">
        <span className="insp-label">Метка</span>
        {tag.inline}
      </div>
      {tag.editor}
      <p className="insp-hint">По метке ордер найдут проверки и отмены.</p>
      {orderSummary()}
      <p className="insp-hint">
        При смене стороны введённые значения сохраняются. Продажа всего актива при переходе к
        покупке выбирает сумму: задайте бюджет покупки.
      </p>
      {(['amount', 'quantity', 'price'] as const).filter(
        (key) =>
          block[key] && (key === 'price' ? !showsOrderPrice(block) : key !== orderSizeMode(block)),
      ).length > 0 && (
        <section aria-label="Сохранённые настройки другого режима" className="insp-saved">
          <p className="insp-hint">
            Эти значения сохранены для возврата к другому режиму и не исполняются. Их ссылки
            удерживают параметры и именованные значения до очистки.
          </p>
          {(['amount', 'quantity', 'price'] as const)
            .filter(
              (key) =>
                block[key] &&
                (key === 'price' ? !showsOrderPrice(block) : key !== orderSizeMode(block)),
            )
            .map((key) => (
              <div key={key}>
                {expression(
                  `Сохранённое ${sizeNames[key]}`,
                  key,
                  key === 'amount' ? 'QuoteAmount' : key === 'quantity' ? 'BaseAmount' : 'Price',
                )}
                <button type="button" className="insp-link" onClick={() => change({ [key]: null })}>
                  Очистить сохранённое {sizeAccusative[key]}
                </button>
              </div>
            ))}
        </section>
      )}
    </div>
  );

  const tagText = expressionText(block.tag, definition);
  const tagOwners = definition.blocks.filter(
    (item) =>
      item.kind === 'order' && block.tag && expressionText(item.tag, definition) === tagText,
  );
  const cancelPart = block.kind === 'cancel' && (
    <div className="insp-group" role="group" aria-label="Что отменяет">
      <h4 className="insp-sub">Что отменяет</h4>
      <div className="insp-row">
        <span className="insp-label">Метка</span>
        {tag.inline}
      </div>
      {tag.editor}
      <div className="insp-card">
        <span className="insp-hint">Эту метку ставит:</span>
        {tagOwners.length > 0 ? (
          <div className="insp-chips">{tagOwners.map((item) => blockChip(item.id))}</div>
        ) : (
          <span className="insp-text">ни один ордер в стратегии.</span>
        )}
      </div>
    </div>
  );

  const exits = blockExits(block);
  const outputStep = (
    <Step
      index={3}
      name="Выход"
      last
      title={block.kind === 'order' ? 'Выход · что может случиться' : 'Выход · что дальше'}
    >
      {exits.map((exit) => {
        const targets = exitTargets(definition, block.id, exit);
        const name = eventExitLabel(exit) || 'запускает';
        return (
          <div key={exit} className="insp-exit" role="group" aria-label={`Выход ${name}`}>
            <span className="insp-exit-name">
              {name}
              {exitHints[block.kind]?.[exit] && (
                <span className="insp-hint is-block">{exitHints[block.kind]![exit]}</span>
              )}
            </span>
            <span className="insp-arrow" aria-hidden="true">
              →
            </span>
            <span className="insp-chips is-column">
              {targets.map((target) => blockChip(target))}
              {targets.length === 0 && <span className="insp-muted">ничего</span>}
              <button
                type="button"
                className="insp-link"
                aria-label={`Подключить блок к выходу «${name}»`}
                onClick={() => editor.openPicker({ nodeId: block.id, port: exit })}
              >
                {targets.length === 0 ? '+ Подключить' : '+ Подключить ещё'}
              </button>
            </span>
          </div>
        );
      })}
    </Step>
  );

  return (
    <div className={`insp tone-${blockTone(block)}`}>
      <header className="insp-head">
        <div className="insp-kind">
          <span className="insp-sq" aria-hidden="true" />
          {kindLines[block.kind] ?? block.kind}
          {onClose && (
            <button
              type="button"
              aria-label="Закрыть настройки"
              onClick={onClose}
              className="board-button board-icon insp-close"
            >
              ×
            </button>
          )}
        </div>
        <input
          className="insp-name"
          aria-label="Название блока"
          value={block.displayName ?? title}
          onChange={(event) =>
            change({ displayName: event.target.value }, `displayName:${block.id}`)
          }
          onBlur={editor.endHistoryGroup}
        />
        {twins > 0 && (
          <p className="insp-hint">
            В стратегии есть ещё {twins} {twins === 1 ? 'блок' : twins < 5 ? 'блока' : 'блоков'} с
            таким именем. Уникальное имя проще найти на схеме.
          </p>
        )}
      </header>
      <div className="insp-content">
        {diagnostics
          .filter((item) => item.blockId === block.id && item.checkIndex == null)
          .map((item, index) => (
            <p key={index} role="alert" className="insp-error">
              {item.message}
            </p>
          ))}
        {Object.entries(fieldErrors).flatMap(([field, messages]) =>
          messages.map((message, index) => (
            <p key={`${field}-${index}`} role="alert" className="insp-error">
              {message}
            </p>
          )),
        )}
        {error && (
          <p role="alert" className="insp-error">
            {error}
          </p>
        )}
        {inputStep}
        <Step index={2} name="Внутри" title="Внутри">
          {valuesPart}
          {eventPart}
          {conditionPart}
          {orderPart}
          {cancelPart}
        </Step>
        {outputStep}
      </div>
    </div>
  );
}
