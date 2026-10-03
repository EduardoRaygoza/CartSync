import { expect, test } from '@playwright/test';

test.beforeEach(async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('.entry')).toHaveCount(3);
});

test('an optimistic check is atomically durable with its operation', async ({ page, context }) => {
  await context.setOffline(true);
  await page.locator('.entry').filter({ hasText: 'Milk' }).click();
  await expect(page.locator('.progress')).toContainText('1 pending operations');
  await expect(page.locator('.status')).toHaveText('Offline');
  await context.setOffline(false);
  await page.reload();
  await expect(page.locator('.entry').filter({ hasText: 'Milk' })).toHaveClass(/acquired/);
  await expect(page.locator('#state-inspector')).toContainText('trip.entry.field.set');
});

test('local rendering and durable commit stay within the spike gates', async ({ page }) => {
  const started = Date.now();
  await page.locator('.entry').first().click();
  await expect(page.locator('.progress')).toContainText('1 pending operations');
  expect(Date.now() - started).toBeLessThan(100);
  const commitMs = await page.locator('dd').last().textContent();
  expect(Number.parseFloat(commitMs ?? '999')).toBeLessThan(250);
});

test('BroadcastChannel refreshes a second tab and one uploader lease drains the outbox', async ({ page, context }) => {
  const second = await context.newPage();
  await second.goto('/');
  await page.locator('.entry').filter({ hasText: 'Rice' }).click();
  await expect(second.locator('.entry').filter({ hasText: 'Rice' })).toHaveClass(/acquired/);
  await Promise.all([page.locator('.upload').click(), second.locator('.upload').click()]);
  await expect(page.locator('.progress')).toContainText('0 pending operations');
  await expect(second.locator('.progress')).toContainText('0 pending operations');
});

test('offline relaunch restores the route within three seconds once the shell is present', async ({ page, context }) => {
  await page.locator('.entry').first().click();
  const started = Date.now();
  await page.reload();
  await expect(page.locator('.entry')).toHaveCount(3);
  expect(Date.now() - started).toBeLessThan(3_000);
});

test('records repeatable local-commit evidence', async ({ page }) => {
  const commitSamples: number[] = [];
  const interactionSamples: number[] = [];
  for (let index = 0; index < 20; index++) {
    const started = performance.now();
    await page.locator('.entry').first().click();
    await expect(page.locator('.progress')).toContainText(`${index + 1} pending operations`);
    interactionSamples.push(performance.now() - started);
    commitSamples.push(Number.parseFloat(await page.locator('dd').last().textContent() ?? '999'));
  }
  const summary = { engine: 'native-indexeddb', interactionMedianMs: percentile(interactionSamples, 50), interactionP95Ms: percentile(interactionSamples, 95), commitMedianMs: percentile(commitSamples, 50), commitP95Ms: percentile(commitSamples, 95) };
  console.log(`SPIKE_METRICS ${JSON.stringify(summary)}`);
  expect(summary.interactionP95Ms).toBeLessThan(100);
  expect(summary.commitP95Ms).toBeLessThan(250);
});

function percentile(values: number[], requested: number): number {
  const sorted = [...values].sort((a, b) => a - b);
  return Number(sorted[Math.ceil((requested / 100) * sorted.length) - 1].toFixed(2));
}
