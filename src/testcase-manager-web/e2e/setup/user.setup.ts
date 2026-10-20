/// <reference types="node" />
import { expect, test as setup } from '@playwright/test';

// Create reusable signed-in user browser state for the user test project.
setup('authenticate as user', async ({ page }) => { 
    // Setup Step 1: Authenticate as user
    const username = process.env.ATCM_E2E_USER;
    const password = process.env.ATCM_E2E_USER_PASSWORD;
    if (!username || !password) throw new Error ('User test credentials are missing.');

    // Setup Step 2: Navigate to the login page and perform login
    await page.goto('/login');
    await expect(page.getByRole('heading', { name: 'Sign in to ATCM' })).toBeVisible();
    await page.getByLabel('Username').fill(username);
    await page.getByLabel('Password').fill(password);
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expect(page).toHaveURL('http://localhost:4301/');
    await expect(page.getByRole('heading', { name: 'Your test workspace' })).toBeVisible();

    // Setup Step 3: Verify that the user is authenticated and has the correct role
    const response = await page.request.get('/api/auth/me');
    expect(response.status()).toBe(200);
    const account = await response.json();
    expect(account.isAdmin).toBe(false);

    await page.context().storageState({ path: 'e2e/.auth/user.json' });

});
