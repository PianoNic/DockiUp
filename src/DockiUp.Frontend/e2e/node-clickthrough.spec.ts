import { test, expect } from '@playwright/test';

// Full UI click-through for the multi-server flow: deploy to a node via the wizard, operate the
// node-hosted container from the detail menu, and open a web terminal into it. Requires the control
// plane serving the SPA on :5098 with one online node ("e2e-node").
test('deploy to a node, operate the container, and open its terminal — all via the UI', async ({ page }) => {
  test.setTimeout(90000);
  const compose =
    'services:\n  web:\n    image: mcr.microsoft.com/dotnet/runtime-deps:10.0\n    command: ["sleep", "3600"]';

  await page.goto('/', { waitUntil: 'domcontentloaded' });

  // ---- Deploy wizard ----
  await page.getByRole('button', { name: /Deploy a new project/i }).first().click();
  // Source -> Where & environment -> Updates -> Review (validated on the node before creating).
  await page.getByRole('radio', { name: /^Compose/ }).click();
  await page.getByLabel('Compose file').fill(compose);
  await page.getByRole('button', { name: 'Next' }).click();

  await page.getByLabel('Project Name').fill('uinode');
  await page.getByLabel('Deploy to').click();
  await page.getByRole('option', { name: 'e2e-node' }).click();
  await page.getByRole('button', { name: 'Next' }).click();

  await page.locator('mat-radio-button:has-text("Update Manually")').click();
  await page.getByRole('button', { name: 'Next' }).click();
  await expect(page.getByText('Valid', { exact: true })).toBeVisible({ timeout: 20000 });

  // Capture the deploy call so a server-side failure is visible instead of a silent empty list.
  const deployResp = page.waitForResponse((r) => r.url().includes('/api/Project/DeployProject'), { timeout: 20000 });
  await page.getByRole('button', { name: 'Create' }).click();
  const resp = await deployResp;
  const body = await resp.text().catch(() => '');
  console.log(`DEPLOY status=${resp.status()} body=${body.slice(0, 300)}`);
  expect(resp.status(), `deploy failed: ${body.slice(0, 300)}`).toBe(200);

  // ---- Detail page: wait for the node-hosted container to come up ----
  const card = page.locator('.containers-list mat-card');
  await expect(async () => {
    await page.goto('/project/uinode', { waitUntil: 'domcontentloaded' });
    await expect(card.first()).toContainText('State: Running', { timeout: 3000 });
  }).toPass({ timeout: 40000 });

  // ---- Stop via the container menu, then Start ('Start' is exact so it doesn't match 'Restart') ----
  await page.locator('.container-menu-btn').first().click();
  await page.getByRole('menuitem', { name: 'Stop', exact: true }).click();
  await expect(card.first()).toContainText('State: Stopped', { timeout: 20000 });

  await page.locator('.container-menu-btn').first().click();
  await page.getByRole('menuitem', { name: 'Start', exact: true }).click();
  await expect(card.first()).toContainText('State: Running', { timeout: 20000 });

  // ---- Open the terminal (carries the node id) and run a command on the node ----
  await page.locator('.container-menu-btn').first().click();
  await page.getByRole('menuitem', { name: 'Terminal', exact: true }).click();
  await expect(page).toHaveURL(/\/terminal\/.*nodeId=/);
  await expect(page.locator('.status')).toHaveText('open', { timeout: 20000 });

  await page.locator('.xterm-screen').click();
  await page.keyboard.type('echo UICLICK=$((6*7))');
  await page.keyboard.press('Enter');
  await expect(page.locator('.xterm-rows')).toContainText('UICLICK=42', { timeout: 15000 });

  await page.screenshot({ path: 'dockiup-clickthrough.png', fullPage: true });
});
