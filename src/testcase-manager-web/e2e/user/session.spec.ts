import {expect, test} from '@playwright/test';

// Login & Permissions Scenario 7: Logout clears access
test('logout clears access', async ({ page }) => {
    // Test Step 1: Logout and verify that the user is redirected to the login page
    await page.goto('/');
    await page.getByRole('button', { name: 'Log out' }).click();
    await expect(page).toHaveURL('http://localhost:4301/login');
    await expect(page.getByRole('heading', { name: 'Sign in to ATCM' })).toBeVisible();

    // Test Step 2: Verify that the user is no longer authenticated and cannot access protected resources
    const response = await page.request.get('/api/auth/me');
    expect(response.status()).toBe(401); // API authorization guard against unauthenticated access
});

// Login & Permissions Scenario 8: Signed-in user visiting /login is redirected to the home page
test('signed-in user visiting /login is redirected to the home page', async ({ page }) => {
    // Test Step 1: Visit /login and verify that the user is redirected to the home page
    await page.goto('/login');
    await expect(page).toHaveURL('http://localhost:4301/');
    await expect(page.getByRole('heading', { name: 'Your test workspace' })).toBeVisible();
});