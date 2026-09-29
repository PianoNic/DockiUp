import { test, expect } from '@playwright/test';

// Needs the API running with PUBLIC_URL set and at least one node connected (see docs in README).
test('nodes page lists a live node, pings it, and drafts a node compose', async ({ page }) => {
  await page.goto('/nodes', { waitUntil: 'networkidle' });
  await expect(page.getByRole('heading', { name: 'Nodes' })).toBeVisible();

  const row = page.locator('tbody tr').filter({ hasText: 'online' }).first();
  await expect(row).toBeVisible();
  await row.getByRole('button', { name: 'Ping' }).click();
  await expect(row).toContainText(/pong · \d+ms/);

  await page.getByRole('button', { name: 'Add node' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.locator('pre')).toContainText('Node__ServerUrl');
  await expect(dialog.locator('pre')).toContainText(/Node__Token: "[\w-]{20,}"/);
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  await expect(dialog).toBeHidden();
});
