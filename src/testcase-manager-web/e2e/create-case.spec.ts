import { expect, test } from '@playwright/test';

test('create a project, module and multi-step case, then reopen it', async ({ page }) => {
  const projectName = `Browser test ${Date.now()}`;
  await page.goto('/');
  await page.getByRole('button', { name: '+ New project', exact: true }).click();
  let dialog = page.getByRole('dialog');
  await dialog.getByLabel('Name', { exact: false }).fill(projectName);
  await dialog.getByRole('button', { name: 'Save project', exact: true }).click();
  await expect(dialog).not.toBeVisible();
  await expect(page).toHaveURL(/\/projects\/browser-test-\d+--\d+$/);
  await expect(page.getByRole('heading', { name: 'Recently opened projects' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: '+ New project', exact: true })).toHaveCount(0);
  await expect(page.locator('header.page-heading').getByRole('button', { name: 'Delete project' })).toBeVisible();
  await expect(page.getByRole('button', { name: /Projects/ })).not.toContainText('(');

  await page.getByRole('button', { name: '+ New module', exact: true }).click();
  dialog = page.getByRole('dialog');
  await dialog.getByLabel('Name', { exact: false }).fill('Authentication');
  await dialog.getByRole('button', { name: 'Save module', exact: true }).click();
  await expect(dialog).not.toBeVisible();

  await page.getByRole('button', { name: '+ New test case', exact: true }).last().click();
  dialog = page.getByRole('dialog');
  // A blank submission must stay on the form and explain the problem.
  await dialog.getByRole('button', { name: 'Save test case', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText('required');
  await dialog.getByLabel('Title').fill('Login with valid credentials');
  await dialog
    .getByLabel('Description', { exact: true })
    .fill('A synthetic browser acceptance test.');
  await dialog.getByLabel('Preconditions').fill('An active account exists.');
  await dialog.getByLabel('Action', { exact: false }).fill('Open login');
  await dialog.getByLabel('Expected result', { exact: false }).fill('The login form appears');
  await dialog.getByRole('button', { name: '+ Add step', exact: true }).click();
  await dialog.getByLabel('Action', { exact: false }).nth(1).fill('Submit valid credentials');
  await dialog
    .getByLabel('Expected result', { exact: false })
    .nth(1)
    .fill('The account page appears');
  await dialog.getByRole('button', { name: 'Save test case', exact: true }).click();
  await expect(page).toHaveURL(/\/projects\/browser-test-\d+--\d+\/modules\/authentication--\d+\/cases\/\d+$/);
  await expect(page.getByRole('heading', { name: 'Login with valid credentials' })).toBeVisible();
  await expect(page.locator('.saved-step')).toHaveCount(2);
  await expect(page.locator('.saved-step').first()).toContainText('Open login');
  await page.getByRole('button', { name: 'Remove step 1' }).click();
  const removeDialog = page.getByRole('dialog', { name: 'Remove step 1?' });
  await expect(removeDialog).toBeVisible();
  await removeDialog.getByRole('button', { name: 'Cancel' }).click();
  await expect(page.locator('.saved-step')).toHaveCount(2);
  await page.getByRole('checkbox', { name: '✓ Passed' }).first().check();
  await page.getByRole('button', { name: '✕ Failed' }).nth(1).click();
  const failureDialog = page.getByRole('dialog', { name: 'Actual Result' });
  await expect(failureDialog).toBeVisible();
  await failureDialog.getByLabel('Actual test result').fill('An error page appeared');
  await failureDialog.getByRole('group', { name: 'Can this issue be replicated?' }).getByLabel('Yes').check();
  await failureDialog.getByRole('group', { name: 'Are you the only user affected?' }).getByLabel('No').check();
  await failureDialog.getByRole('button', { name: 'Record failed step' }).click();
  await page.getByRole('button', { name: 'Save completed run' }).click();
  await expect(page.getByRole('status')).toContainText('Run saved: Failed.');
  await page.reload();
  await expect(page.locator('.saved-step').nth(1)).toContainText('The account page appears');
  await expect(page.getByText('Previous runs')).toBeVisible();
  await expect(page.locator('.run-history')).toHaveCount(1);
  await page.locator('.run-history summary').click();
  await expect(page.locator('.run-history')).toContainText('An error page appeared');
  await page.getByRole('button', { name: '+ Add Actions' }).click();
  const addActionDialog = page.getByRole('dialog', { name: 'Add Action' });
  await addActionDialog.getByRole('button', { name: 'Add Action' }).click();
  await expect(addActionDialog.getByRole('alert')).toContainText('Enter an action');
  await addActionDialog.locator('#new-action').fill('Check the profile page');
  await addActionDialog.locator('#new-expected-result').fill('Profile details appear');
  await addActionDialog.getByRole('button', { name: 'Add Action' }).click();
  await expect(page.locator('.saved-step')).toHaveCount(3);
  await expect(page.locator('.saved-step').last()).toContainText('Check the profile page');
  await expect(page.locator('.run-history')).toContainText('2 steps');
  await page.getByRole('button', { name: 'Remove step 3' }).click();
  await page.getByRole('dialog', { name: 'Remove step 3?' }).getByRole('button', { name: 'Remove step' }).click();
  await expect(page.locator('.saved-step')).toHaveCount(2);
  await expect(page.getByRole('link', { name: projectName, exact: true })).toBeVisible();
  await page.screenshot({ path: 'test-results/case-detail.png', fullPage: true });
  await page.getByRole('link', { name: `Back to ${projectName}`, exact: false }).click();
  await expect(page).toHaveURL(/\/projects\/browser-test-\d+--\d+$/);
  await expect(
    page.getByRole('link', { name: 'Login with valid credentials' }).first(),
  ).toBeVisible();
  const editLink = page.getByRole('link', { name: 'Open test case Login with valid credentials' });
  await expect(editLink).toBeVisible();
  await editLink.click();
  await expect(page.getByRole('heading', { name: 'Login with valid credentials' })).toBeVisible();
  await page.getByRole('button', { name: 'Remove step 1' }).click();
  await page.getByRole('dialog', { name: 'Remove step 1?' }).getByRole('button', { name: 'Remove step' }).click();
  await expect(page.locator('.saved-step')).toHaveCount(1);
  await page.getByRole('button', { name: 'Delete test case by removing last step' }).click();
  const deleteDialog = page.getByRole('dialog', { name: 'Delete test case?' });
  await expect(deleteDialog).toContainText('all saved run history');
  await expect(deleteDialog.getByRole('button', { name: 'Delete test case' })).toBeDisabled();
  await deleteDialog.getByLabel('Type Confirm to confirm').fill('confirm');
  await expect(deleteDialog.getByRole('button', { name: 'Delete test case' })).toBeDisabled();
  await deleteDialog.getByLabel('Type Confirm to confirm').fill('Confirm');
  await deleteDialog.getByRole('button', { name: 'Delete test case' }).click();
  await expect(page).toHaveURL(/\/projects\/browser-test-\d+--\d+/);
  await expect(page.getByRole('link', { name: 'Login with valid credentials' })).toHaveCount(0);
  await page.screenshot({ path: 'test-results/workspace.png', fullPage: true });
});

test('empty projects use an in-app deletion confirmation', async ({ page }) => {
  const name = `Delete check ${Date.now()}`;
  await page.goto('/');
  await page.getByRole('button', { name: '+ New project', exact: true }).click();
  const editor = page.getByRole('dialog', { name: 'Create project' });
  await editor.getByLabel('Name').fill(name);
  await editor.getByRole('button', { name: 'Save project' }).click();
  await expect(page.getByRole('heading', { name })).toBeVisible();
  await page.getByRole('button', { name: 'Delete project' }).click();
  const confirmation = page.getByRole('dialog', { name: 'Delete project?' });
  await expect(confirmation).toContainText(name);
  await confirmation.getByRole('button', { name: 'Cancel' }).click();
  await expect(confirmation).not.toBeVisible();
  await expect(page.getByRole('heading', { name })).toBeVisible();
  await page.getByRole('main').getByRole('button', { name: 'Delete project' }).click();
  await confirmation.getByRole('button', { name: 'Delete project' }).click();
  await expect(page).toHaveURL('/');
  await expect(page.getByRole('link', { name })).toHaveCount(0);
});

test('a saved failed run allows completion, then the case is view-only', async ({ page }) => {
  const projectName = `Complete check ${Date.now()}`;
  await page.goto('/');
  await page.getByRole('button', { name: '+ New project', exact: true }).click();
  let dialog = page.getByRole('dialog', { name: 'Create project' });
  await dialog.getByLabel('Name').fill(projectName);
  await dialog.getByRole('button', { name: 'Save project' }).click();
  await page.getByRole('button', { name: '+ New module' }).click();
  dialog = page.getByRole('dialog', { name: 'Create module' });
  await dialog.getByLabel('Name').fill('Payments');
  await dialog.getByRole('button', { name: 'Save module' }).click();
  await page.getByRole('button', { name: '+ New test case' }).click();
  dialog = page.getByRole('dialog', { name: 'Create test case' });
  await dialog.getByLabel('Title').fill('Card payment');
  await dialog.getByRole('combobox', { name: 'Status' }).selectOption('Ready');
  await dialog.getByLabel('Action', { exact: false }).fill('Submit payment');
  await dialog.getByLabel('Expected result', { exact: false }).fill('Confirmation appears');
  await dialog.getByRole('button', { name: 'Save test case' }).click();

  await expect(page.getByRole('button', { name: 'Mark complete' })).toBeDisabled();
  await page.getByRole('button', { name: '✕ Failed' }).click();
  dialog = page.getByRole('dialog', { name: 'Actual Result' });
  await dialog.getByLabel('Actual test result').fill('Error message appears');
  await dialog.getByRole('group', { name: 'Can this issue be replicated?' }).getByLabel('Yes').check();
  await dialog.getByRole('group', { name: 'Are you the only user affected?' }).getByLabel('No').check();
  await dialog.getByRole('button', { name: 'Record failed step' }).click();
  await page.getByRole('button', { name: 'Save completed run' }).click();
  await expect(page.getByRole('button', { name: 'Mark complete' })).toBeEnabled();
  await page.getByRole('button', { name: 'Mark complete' }).click();
  await expect(page.locator('.case-heading .badge')).toHaveText('Complete');
  await expect(page.getByRole('button', { name: '+ Add Actions' })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Delete test case by removing last step' })).toBeDisabled();
  await expect(page.getByRole('button', { name: '✕ Failed' })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Save completed run' })).toBeDisabled();
  await expect(page.locator('.run-history')).toHaveCount(1);
  await page.reload();
  await expect(page.locator('.case-heading .badge')).toHaveText('Complete');
  await expect(page.getByRole('button', { name: '+ Add Actions' })).toBeDisabled();
  await page.getByRole('button', { name: 'Delete test case', exact: true }).click();
  const confirmation = page.getByRole('dialog', { name: 'Delete test case?' });
  await confirmation.getByLabel('Type Confirm to confirm').fill('Confirm');
  await confirmation.getByRole('button', { name: 'Delete test case' }).click();
  await expect(page.getByRole('link', { name: 'Card payment' })).toHaveCount(0);
});
