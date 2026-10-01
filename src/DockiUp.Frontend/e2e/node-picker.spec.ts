import { test, expect } from '@playwright/test';

// Verifies the create-project modal surfaces online nodes as deploy targets (Phase 2c). Requires the
// control plane API on :5098 with at least one online node ("e2e-node") registered.
test('create-project modal offers an online node as a deploy target', async ({ page }) => {
  // SignalR holds a long-lived connection, so 'networkidle' never settles — wait for DOM instead.
  await page.goto('/', { waitUntil: 'domcontentloaded' });

  // Open the deploy modal (the button appears in both the toolbar and the dashboard).
  await page.getByRole('button', { name: /Deploy a new project/i }).first().click();
  await page.getByRole('radio', { name: /^Compose/ }).click();
  await page.getByLabel('Compose file').fill('services:\n  web:\n    image: nginx');
  await page.getByRole('button', { name: 'Next' }).click();

  // "Where & environment" shows the node selector (only rendered when a node is online).
  const deployTo = page.getByLabel('Deploy to');
  await expect(deployTo).toBeVisible();

  await deployTo.click();
  // The default local option plus the registered node.
  await expect(page.getByRole('option', { name: 'Local (this host)' })).toBeVisible();
  await expect(page.getByRole('option', { name: 'e2e-node' })).toBeVisible();

  await page.getByRole('option', { name: 'e2e-node' }).click();
  await page.screenshot({ path: 'dockiup-node-picker.png', fullPage: true });
});
