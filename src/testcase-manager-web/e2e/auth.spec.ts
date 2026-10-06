import { expect, test } from '@playwright/test';

// Auth Scenario 1: An unauthenticated visitor is sent to sign in
test('Unauthenticated visitor is sent to sign in', async ({ page }) => {
  await page.goto('/');

  await expect(page).toHaveURL(/\/login$/);
  await expect(
    page.getByRole('heading', { name: 'Sign in to ATCM' })
  ).toBeVisible();
});

// Auth Scenario 2: An unauthenticated visitor is sent to sign in when login form is submitted empty
test('Login form is submitted empty', async ({ page }) => {
  await page.goto('/login');
  await expect(page.getByRole('heading', { name: 'Sign in to ATCM' })).toBeVisible();
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL(/\/login$/);
});

// Auth Scenario 3: An unauthenticated visitor is sent to sign in when login form is submitted with invalid credentials
test ('Login form is submitted with invalid credentials', async ({ page }) => {
  await page.goto('/login');
  await expect(page.getByRole('heading', { name: 'Sign in to ATCM' })).toBeVisible();
  await page.getByLabel('Username').fill('invalidabdi');
  await page.getByLabel('Password').fill('invalidpassword');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL(/\/login$/);
  await expect(page.getByRole('alert')).toHaveText('Invalid username or password.');
});