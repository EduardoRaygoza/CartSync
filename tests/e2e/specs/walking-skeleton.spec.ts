import { expect, test } from '@playwright/test';

test('signs in through the test seam and loads the authorized empty scope', async ({ page }) => {
  await page.route('**/runtime-config.json', route => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify({
      environment: 'test',
      apiOrigin: 'https://api.test',
      externalIdAuthority: 'https://tenant.ciamlogin.com/tenant.onmicrosoft.com',
      externalIdClientId: 'client-id',
      externalIdScope: 'api://api-id/access_as_user',
      buildVersion: 'test-build',
    }),
  }));
  await page.route('https://api.test/api/v1/sync/bootstrap', route => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify({
      projections: [], aliases: [], syncIssues: [], terminal: true,
      terminalHouseholdCursor: 0, nextPageToken: null,
    }),
  }));

  await page.goto('/');
  await page.getByRole('button', { name: 'Sign in with email' }).click();
  await expect(page.getByText('Connected. Your authorized CartSync scope is ready.')).toBeVisible();
});
