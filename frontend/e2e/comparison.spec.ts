import { randomUUID } from 'node:crypto';

import { expect, test } from '@playwright/test';

// The stage 4 comparison criterion (roadmap): two runs and an optimization trial on one screen. The runs are the
// optimization's finalists, so the whole path runs in the Worker against the local Bybit stub.
test('two runs and an optimization trial compare side by side', async ({ page }) => {
  test.setTimeout(180_000);

  await page.goto('/strategies');
  await page.getByRole('button', { name: 'Новая стратегия' }).first().click();
  await page
    .getByRole('region', { name: 'Шаблоны' })
    .getByRole('button', { name: /^SMA Crossover\b/ })
    .click();
  await page.getByLabel('Название стратегии').fill(`E2E comparison ${randomUUID()}`);
  await page.getByRole('button', { name: 'Создать стратегию', exact: true }).click();
  await expect(page).toHaveURL(/\/strategies\/([0-9a-f-]+)\/edit$/);
  const strategyId = /\/strategies\/([0-9a-f-]+)\/edit$/.exec(page.url())![1];

  await page.goto(`/strategies/${strategyId}/optimize`);
  const setup = page.getByRole('form', { name: 'Настройка оптимизации' });
  await expect(setup.getByLabel('Площадка')).toHaveValue('bybit');
  await setup.getByLabel('Пара').fill('ETHUSDT');
  await setup.getByLabel('Таймфрейм').selectOption('1h');
  await setup.getByLabel('С (UTC), пусто — с начала истории').fill('2026-01-02');
  await setup.getByLabel('По (UTC), пусто — до последней закрытой свечи').fill('2026-01-08');
  const first = setup.getByRole('group').first();
  await first.getByRole('checkbox').check();
  await first.getByLabel('До').fill('15');
  await first.getByLabel('От').fill('10');
  await setup.getByLabel('Сделок не меньше').fill('0');
  await setup.getByLabel('Лучших испытаний прогнать полностью').fill('2');
  await setup.getByRole('button', { name: 'Запустить оптимизацию' }).click();
  await expect(page).toHaveURL(/\/optimizations\/[0-9a-f-]+$/);
  const optimizationUrl = page.url();
  await expect(page.getByRole('status').filter({ hasText: 'Завершена' })).toBeVisible({
    timeout: 120_000,
  });

  for (const finalist of ['Финалист 1', 'Финалист 2']) {
    await page.goto(optimizationUrl);
    await page.getByRole('link', { name: finalist }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Завершена' })).toBeVisible({
      timeout: 90_000,
    });
  }

  await page.goto('/backtests');
  const ranking = page.getByRole('table');
  await ranking.getByRole('checkbox').nth(0).check();
  await ranking.getByRole('checkbox').nth(1).check();
  await page.goto(optimizationUrl);
  await page.getByRole('checkbox', { name: 'Сравнить испытание 1' }).check();
  const bar = page.getByRole('complementary', { name: 'Сравнение' });
  await expect(bar.getByText('Выбрано для сравнения: 3')).toBeVisible();
  await bar.getByRole('link', { name: 'Сравнить' }).click();

  await expect(page).toHaveURL(/\/compare\?/);
  const table = page.getByRole('table', { name: 'Сравнение' });
  await expect(table.getByRole('columnheader')).toHaveCount(4);
  await expect(table.getByRole('link', { name: /^Прогон ETHUSDT/ })).toHaveCount(2);
  await expect(table.getByRole('link', { name: 'Испытание №1' })).toBeVisible();
  await expect(page.getByRole('figure', { name: 'Капитал сравниваемых прогонов' })).toHaveAttribute(
    'data-lines',
    '2',
  );
});
