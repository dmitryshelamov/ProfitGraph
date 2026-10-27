import { randomUUID } from 'node:crypto';

import type { Page } from '@playwright/test';
import { expect, test } from '@playwright/test';

import type { components } from '../src/shared/api/generated/schema';

async function createSma(page: Page) {
  await page.goto('/strategies');
  await page.getByRole('button', { name: 'Новая стратегия' }).first().click();
  await page
    .getByRole('region', { name: 'Шаблоны' })
    .getByRole('button', { name: /^SMA Crossover\b/ })
    .click();
  await page.getByLabel('Название стратегии').fill(`E2E SMA ${randomUUID()}`);
  await page.getByRole('button', { name: 'Создать стратегию', exact: true }).click();
  await expect(page).toHaveURL(/\/strategies\/[0-9a-f-]+\/edit$/);
  await expect(page.locator('.board-state').getByText('v1', { exact: true })).toBeVisible();
  return new URL(page.url()).pathname.replace(/\/edit$/, '');
}

// Order settings show as short rows; their editors open on demand.
const expand = async (page: Page) => {
  const collapsed = page.getByRole('button', { name: /^Изменить: / });
  while ((await collapsed.count()) > 0) {
    await collapsed.first().click();
  }
};
const choose = async (page: Page, group: string, option: string) => {
  await page
    .getByRole('radiogroup', { name: group, exact: true })
    .getByText(option, { exact: true })
    .click();
  await expand(page);
};
const sizeIs = (page: Page, option: string) =>
  expect(
    page
      .getByRole('radiogroup', { name: 'Размер ордера', exact: true })
      .getByRole('radio', { name: option, exact: true }),
  ).toBeChecked();

test('SMA adds a decision after the buy signal, validates and saves the graph', async ({
  page,
}, testInfo) => {
  const path = await createSma(page);
  const rules = page.getByRole('region', { name: 'Блок-схема стратегии' });
  await expect(rules.locator('.react-flow')).toBeVisible();
  await expect(rules.locator('[data-flowchart-node="buySignal"]')).toBeVisible();
  const decisions = rules.locator('.flowchart-decision');
  await expect(decisions).toHaveCount(2);
  await page.screenshot({ path: testInfo.outputPath('sma-canvas.png') });
  const before = await page.request.get(`/api${path}`);
  const original = (await before.json()) as components['schemas']['StrategyResponse'];
  const originalCount = original.definition.blocks.filter(
    (block) => block.kind === 'condition',
  ).length;

  await rules.getByRole('button', { name: 'Добавить после buySignal нет' }).click();
  await rules
    .getByRole('dialog', { name: 'Добавить блок', exact: true })
    .getByRole('button', { name: 'Если', exact: true })
    .click();
  await expect(page.getByText('Есть несохранённые изменения', { exact: true })).toBeVisible();
  await expect(decisions).toHaveCount(3);
  await page.getByRole('button', { name: 'Отменить', exact: true }).click();
  await expect(decisions).toHaveCount(2);
  await page.getByRole('button', { name: 'Повторить', exact: true }).click();
  await expect(decisions).toHaveCount(3);
  await page.getByRole('button', { name: '+ Проверка', exact: true }).click();
  await page.getByRole('menuitem', { name: 'Позиция закрыта', exact: true }).click();

  await page.getByRole('button', { name: 'Проверить', exact: true }).click();
  await expect(page.getByText('Граф корректен', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Сохранить', exact: true }).click();
  await expect(page.locator('.board-state').getByText('v2', { exact: true })).toBeVisible();
  await page.reload();
  await expect(decisions).toHaveCount(3);

  const saved = await page.request.get(`/api${path}`);
  const graph = ((await saved.json()) as components['schemas']['StrategyResponse']).definition;
  expect(graph.blocks.filter((block) => block.kind === 'condition')).toHaveLength(
    originalCount + 1,
  );
  expect(graph.wires.some((wire) => wire.to === 'buy' && wire.from.block === 'buySignal')).toBe(
    true,
  );
});

test('shared indicator values and parameters are edited directly without appearing on the canvas', async ({
  page,
}) => {
  const path = await createSma(page);
  const rules = page.getByRole('region', { name: 'Блок-схема стратегии' });
  await expect(rules.locator('[data-flowchart-node="fastSma"]')).toHaveCount(0);
  await page.getByRole('tab', { name: 'Параметры и значения', exact: true }).click();
  await page.getByLabel('Значение параметра fastPeriod', { exact: true }).fill('21');
  await page.getByLabel('Значение параметра fastPeriod', { exact: true }).blur();
  await page.getByRole('button', { name: 'Сохранить', exact: true }).click();
  await expect(page.locator('.board-state').getByText('v2', { exact: true })).toBeVisible();
  await page.reload();
  await page.getByRole('tab', { name: 'Параметры и значения', exact: true }).click();
  await expect(page.getByLabel('Значение параметра fastPeriod', { exact: true })).toHaveValue('21');
  await page.getByRole('button', { name: 'Закрыть панель', exact: true }).click();
  await expect(page.getByLabel('Значение параметра fastPeriod', { exact: true })).toBeHidden();
  const saved = await page.request.get(`/api${path}`);
  const definition = ((await saved.json()) as components['schemas']['StrategyResponse']).definition;
  expect(definition.values.filter((value) => value.name === 'fastSma')).toHaveLength(1);
  expect(
    definition.parameters.find((parameter) => parameter.name === 'fastPeriod')?.defaultValue,
  ).toBe('21');
});

test('touchpad wheel and React Flow controls zoom the canvas', async ({ page }) => {
  await createSma(page);
  const rules = page.getByRole('region', { name: 'Блок-схема стратегии' });
  const viewport = rules.locator('.react-flow__viewport');
  const initialTransform = await viewport.evaluate(
    (element) => element.ownerDocument.defaultView!.getComputedStyle(element).transform,
  );

  await rules.locator('.react-flow__pane').hover({ position: { x: 10, y: 10 } });
  await page.mouse.wheel(0, -250);
  await expect(viewport).not.toHaveCSS('transform', initialTransform);
  const wheelTransform = await viewport.evaluate(
    (element) => element.ownerDocument.defaultView!.getComputedStyle(element).transform,
  );

  await rules.getByRole('button', { name: 'Увеличить масштаб' }).click();
  await expect(viewport).not.toHaveCSS('transform', wheelTransform);

  await rules.getByRole('button', { name: 'Показать весь граф' }).click();
  await expect(rules.getByRole('button', { name: 'Уменьшить масштаб' })).toBeVisible();
  await expect(page.getByText('Есть несохранённые изменения', { exact: true })).toHaveCount(0);
});

test('renamed action survives save and reload without changing the order tag', async ({ page }) => {
  const path = await createSma(page);
  await page
    .getByRole('region', { name: 'Блок-схема стратегии' })
    .locator('[data-flowchart-node="buy"] .flowchart-head')
    .click();
  const name = page.getByRole('textbox', { name: 'Название блока' });
  await name.fill('Покупка после пересечения');
  await name.blur();
  await page.getByRole('button', { name: 'Сохранить', exact: true }).click();
  await expect(page.locator('.board-state').getByText('v2', { exact: true })).toBeVisible();
  await page.reload();
  await page
    .getByRole('region', { name: 'Блок-схема стратегии' })
    .locator('[data-flowchart-node="buy"] .flowchart-head')
    .click();
  await expect(name).toHaveValue('Покупка после пересечения');

  const saved = await page.request.get(`/api${path}`);
  const graph = ((await saved.json()) as components['schemas']['StrategyResponse']).definition;
  const order = graph.blocks.find((block) => block.id === 'buy');
  expect(order?.displayName).toBe('Покупка после пересечения');
  expect(order?.tag).toMatchObject({ kind: 'constant', valueType: 'Tag', text: 'sma.entry' });
});

test('leaving a dirty strategy requires confirmation', async ({ page }) => {
  const path = await createSma(page);
  await page.getByLabel('Название стратегии').fill(`Unsaved ${randomUUID()}`);
  await page.getByRole('link', { name: 'К стратегии' }).click();
  const dialog = page.getByRole('dialog', {
    name: 'Есть несохранённые изменения. Уйти без сохранения?',
  });
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: 'Остаться' }).click();
  await expect(page).toHaveURL(new RegExp(`${path}/edit$`));
  await page.getByRole('link', { name: 'К стратегии' }).click();
  await dialog.getByRole('button', { name: 'Уйти' }).click();
  await expect(page).toHaveURL(path);
});

test('a version conflict preserves the dirty block until reload', async ({ page }) => {
  const path = await createSma(page);
  await page
    .getByRole('region', { name: 'Блок-схема стратегии' })
    .locator('[data-flowchart-node="buy"] .flowchart-head')
    .click();
  const name = page.getByRole('textbox', { name: 'Название блока' });
  await name.fill('Новая покупка');
  await name.blur();
  const response = await page.request.get(`/api${path}`);
  const current = (await response.json()) as components['schemas']['StrategyResponse'];
  const concurrent = await page.request.put(`/api${path}`, {
    data: {
      expectedVersion: 1,
      name: `${current.name} external`,
      definition: current.definition,
      layout: current.layout,
    },
  });
  expect(concurrent.ok()).toBe(true);
  await page.getByRole('button', { name: 'Сохранить', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Стратегия изменилась в другом окне');
  await expect(name).toHaveValue('Новая покупка');
  await page.getByRole('button', { name: 'Загрузить актуальную версию', exact: true }).click();
  await expect(page.locator('.board-state').getByText('v2', { exact: true })).toBeVisible();
  await expect(page.getByLabel('Название стратегии')).toHaveValue(`${current.name} external`);
});

test('Ladder opens one connected flowchart started by the run start, a resume and a manual fill, driven by order events', async ({
  page,
}, testInfo) => {
  await page.goto('/strategies');
  await page.getByRole('button', { name: 'Новая стратегия' }).first().click();
  await page.getByRole('button', { name: /^Ladder Вход,/ }).click();
  await page.getByLabel('Название стратегии').fill(`E2E Ladder ${randomUUID()}`);
  await page.getByRole('button', { name: 'Создать стратегию', exact: true }).click();
  await expect(page).toHaveURL(/\/strategies\/[0-9a-f-]+\/edit$/);
  const rules = page.getByRole('region', { name: 'Блок-схема стратегии' });
  await expect(rules.locator('.react-flow')).toBeVisible();
  // The run start, the resume (ADR 0040) and the owner's manual fill (ADR 0037) are the events that start the chart.
  await expect(rules.locator('.flowchart-start')).toHaveCount(3);
  await expect(rules.locator('.flowchart-decision').first()).toBeVisible();
  await expect(rules.locator('[data-flowchart-node="entry"]')).toBeVisible();
  await expect(rules.locator('[data-flowchart-node="cancelExit"]')).toBeVisible();
  await expect
    .poll(() =>
      rules
        .locator('.react-flow__edge')
        .evaluateAll((edges) => edges.map((edge) => edge.getAttribute('data-id'))),
    )
    .toContain(JSON.stringify(['entry', 'filled', 'cancelExit']));
  // A wire from an order status is dashed and names its exit while hovered.
  const filled = rules.locator(
    `.react-flow__edge[data-id='${JSON.stringify(['entry', 'filled', 'cancelExit'])}']`,
  );
  await expect(filled.locator('.react-flow__edge-path')).toHaveAttribute(
    'style',
    /stroke-dasharray: 7(px)?,? 5/,
  );
  await filled.dispatchEvent('mouseover');
  await expect(rules.locator('.flowchart-wire-label')).toHaveText('исполнен');
  await page.screenshot({ path: testInfo.outputPath('ladder-canvas.png') });
});

test('order side and size mode preserve authored expressions through save and reload', async ({
  page,
}) => {
  const path = await createSma(page);
  const openOrder = () =>
    page
      .getByRole('region', { name: 'Блок-схема стратегии' })
      .locator('[data-flowchart-node="buy"] .flowchart-head')
      .click();
  await openOrder();
  await expand(page);
  await choose(page, 'Сторона ордера', 'Продать');
  await sizeIs(page, 'На сумму');
  await expect(page.getByLabel('Ссылка Сумма', { exact: true })).toHaveValue('buyAmount');
  await expect(page.getByLabel('Источник Цена пересчёта', { exact: true })).toHaveValue('candle');
  await choose(page, 'Размер ордера', 'Количество');
  await page.getByLabel('Источник Количество', { exact: true }).selectOption('constant');
  await page.getByLabel('Значение Количество', { exact: true }).fill('0.1234567890123456789');
  await page.getByLabel('Значение Количество', { exact: true }).blur();
  await choose(page, 'Сторона ордера', 'Купить');
  await expect(page.getByLabel('Значение Количество', { exact: true })).toHaveValue(
    '0.1234567890123456789',
  );
  await choose(page, 'Размер ордера', 'На сумму');
  await expect(page.getByLabel('Ссылка Сумма', { exact: true })).toHaveValue('buyAmount');
  await page.getByRole('button', { name: 'Проверить', exact: true }).click();
  await expect(page.getByText('Граф корректен', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Сохранить', exact: true }).click();
  await expect(page.locator('.board-state').getByText('v2', { exact: true })).toBeVisible();
  await page.reload();
  const saved = await page.request.get(`/api${path}`);
  const definition = ((await saved.json()) as components['schemas']['StrategyResponse']).definition;
  const order = definition.blocks.find((block) => block.id === 'buy')!;
  expect(order).toMatchObject({
    side: 'Buy',
    sizeMode: 'amount',
    amount: { kind: 'parameter', name: 'buyAmount' },
    quantity: { kind: 'constant', valueType: 'BaseAmount', number: '0.1234567890123456789' },
    price: { kind: 'candle', field: 'Close' },
  });
  await openOrder();
  await expand(page);
  await choose(page, 'Размер ордера', 'Количество');
  await expect(page.getByLabel('Значение Количество', { exact: true })).toHaveValue(
    '0.1234567890123456789',
  );
});

test('referenced parameters and shared values cannot be deleted before validation', async ({
  page,
}) => {
  await createSma(page);
  await page.getByRole('tab', { name: 'Параметры и значения', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Удалить параметр buyAmount', exact: true }),
  ).toBeDisabled();
  await expect(page.getByText(/Нельзя удалить параметр «buyAmount»/)).toContainText(
    'Покупка по рынку',
  );
  await expect(
    page.getByRole('button', { name: 'Удалить значение close', exact: true }),
  ).toBeDisabled();
  await expect(page.getByText(/Нельзя удалить значение «close»/)).toContainText('fastSma');
  await expect(page.getByRole('button', { name: 'Сохранить', exact: true })).toBeDisabled();
});

test('side and size edits retain remembered inputs while named references survive atomic rename and reload', async ({
  page,
}) => {
  const path = await createSma(page);
  const board = page.getByRole('region', { name: 'Блок-схема стратегии' });
  await board.locator('[data-flowchart-node="sell"] .flowchart-head').click();
  await expand(page);
  await choose(page, 'Сторона ордера', 'Купить');
  await sizeIs(page, 'На сумму');
  await expect(page.getByLabel('Источник Сумма', { exact: true })).toHaveValue('');
  await expect(page.getByLabel('Поле Сохранённое Количество', { exact: true })).toHaveValue(
    'baseAvailable',
  );
  await page.getByLabel('Источник Сумма', { exact: true }).selectOption('parameter');
  await page.getByLabel('Ссылка Сумма', { exact: true }).selectOption('buyAmount');
  await choose(page, 'Размер ордера', 'Количество');
  await page.getByLabel('Источник Количество', { exact: true }).selectOption('constant');
  await page
    .getByLabel('Значение Количество', { exact: true })
    .fill('0.0000000000000000000000000001');
  await page.getByLabel('Значение Количество', { exact: true }).blur();
  await expect(board.locator('[data-flowchart-node="sell"]')).toContainText('по цене сигнала');
  await choose(page, 'Размер ордера', 'На сумму');
  await page.getByRole('tab', { name: 'Параметры и значения', exact: true }).click();
  await page.getByLabel('Новое имя buyAmount', { exact: true }).fill('investment');
  await page
    .getByRole('group', { name: 'Параметр buyAmount', exact: true })
    .getByRole('button', { name: 'Переименовать', exact: true })
    .click();
  await page.getByRole('button', { name: 'Сохранить', exact: true }).click();
  await expect(page.locator('.board-state').getByText('v2', { exact: true })).toBeVisible();
  await page.reload();
  await expect(board.locator('[data-flowchart-node="sell"]')).toBeVisible();
  const response = await page.request.get(`/api${path}`);
  const definition = ((await response.json()) as components['schemas']['StrategyResponse'])
    .definition;
  expect(definition.parameters.some((parameter) => parameter.name === 'investment')).toBe(true);
  const sell = definition.blocks.find((block) => block.id === 'sell')!;
  expect(sell.side).toBe('Buy');
  expect(sell.sizeMode).toBe('amount');
  expect(sell.amount).toMatchObject({ kind: 'parameter', name: 'investment' });
  expect(sell.quantity).toMatchObject({
    kind: 'constant',
    number: '0.0000000000000000000000000001',
  });
  expect(definition.blocks.find((block) => block.id === 'buy')!.amount).toMatchObject({
    kind: 'parameter',
    name: 'investment',
  });
});

test('blocks are added from the palette or a dropped wire and any block can be deleted', async ({
  page,
}) => {
  await createSma(page);
  const rules = page.getByRole('region', { name: 'Блок-схема стратегии' });
  const blocks = rules.locator('[data-flowchart-node]');
  await expect(blocks).toHaveCount(5);

  // Any block, the event included, is deleted by its cross or by the Delete key.
  await rules.locator('[data-flowchart-node="clock"]').hover();
  await rules.getByRole('button', { name: 'Удалить блок clock', exact: true }).click();
  await expect(blocks).toHaveCount(4);
  await page.getByRole('button', { name: 'Отменить', exact: true }).click();
  await expect(blocks).toHaveCount(5);
  await rules.locator('[data-flowchart-node="sell"] .flowchart-head').click();
  await page.keyboard.press('Delete');
  await expect(blocks).toHaveCount(4);
  await page.getByRole('button', { name: 'Отменить', exact: true }).click();

  // A palette item is added by a click or dragged onto the board.
  await rules.getByRole('button', { name: 'Добавить блок', exact: true }).click();
  await rules.getByRole('menuitem', { name: 'Если', exact: true }).click();
  await expect(blocks).toHaveCount(6);
  await expect(page.getByRole('complementary', { name: 'Настройки блока' })).toContainText(
    'Сюда пока ничего не подключено',
  );
  await rules.getByRole('button', { name: 'Добавить блок', exact: true }).click();
  await rules
    .getByRole('menuitem', { name: 'Отмена ордера', exact: true })
    .dragTo(rules.locator('.react-flow__pane'), { targetPosition: { x: 520, y: 820 } });
  await expect(blocks).toHaveCount(7);

  // A wire let go over empty board offers a block and connects it.
  const exit = rules.locator(
    '[data-flowchart-node="sellSignal"] .react-flow__handle[data-handleid="no"]',
  );
  const box = (await exit.boundingBox())!;
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.mouse.down();
  await page.mouse.move(box.x + 160, box.y + 260, { steps: 12 });
  await page.mouse.up();
  await rules
    .getByRole('dialog', { name: 'Добавить блок', exact: true })
    .getByRole('button', { name: 'Продажа по рынку', exact: true })
    .click();
  await expect(blocks).toHaveCount(8);
  await expect(page.getByRole('complementary', { name: 'Настройки блока' })).toContainText(
    'Сигнал на продажу',
  );
});

test('the plus in a connected exit adds one more branch from it', async ({ page }) => {
  await createSma(page);
  const rules = page.getByRole('region', { name: 'Блок-схема стратегии' });
  await expect(rules.locator('.react-flow__edge')).toHaveCount(4);
  await rules.getByRole('button', { name: 'Добавить после buySignal да', exact: true }).click();
  await rules
    .getByRole('dialog', { name: 'Добавить блок', exact: true })
    .getByRole('button', { name: 'Отмена ордера', exact: true })
    .click();
  await expect(rules.locator('[data-flowchart-node]')).toHaveCount(6);
  await expect(rules.locator('.react-flow__edge')).toHaveCount(5);
  await expect(page.getByRole('complementary', { name: 'Настройки блока' })).toContainText(
    'Сигнал на покупку',
  );
});
