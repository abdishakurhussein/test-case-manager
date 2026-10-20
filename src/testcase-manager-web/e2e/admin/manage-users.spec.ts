import {expect, test} from '@playwright/test';

// Login & Permissions Scenario 3: Admin can access user management
test('admin can access user management', async ({ page }) => {
    await page.goto('/');

    // Test Step 1: Assert that manage users link then click on it
    await expect(page.getByRole('link', { name: 'Manage users' })).toBeVisible();
    await page.getByRole('link', { name: 'Manage users' }).click();
    await expect(page).toHaveURL('http://localhost:4301/admin/users');
    await expect(page.getByRole('heading', { name: 'Manage users' })).toBeVisible();

    // Test Step 2: Create user button is visible
    await expect(page.getByRole('button', { name: 'Create user' })).toBeVisible();


});

// Login & Permissions Scenario 4: Admin can request user list from API
test('admin can request user list from API', async ({ page }) => {
    // Test Step 1: Request user list from API
    const response = await page.request.get('/api/admin/users');
    expect(response.status()).toBe(200);
    const users = await response.json();
    expect(users).toBeInstanceOf(Array);
});
