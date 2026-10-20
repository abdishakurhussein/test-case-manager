import { expect, test } from '@playwright/test';

// Test Scenario 1: Authenticate as user
test('authenticate as user', async ({ page }) => { 
    
    // Test Step 1: Open /admin/users and verify that the user is not authorized to access this page
    await page.goto('/admin/users');
    await expect(page).toHaveURL('http://localhost:4301/'); // UI guard

    // Test Step 2: Verify that the user is not authorized to access the /api/admin/users endpoint
    const response = await page.request.get('/api/admin/users');
    expect(response.status()).toBe(403); // API authorization


});

//Test Scenario 2: User cannot see "Manage users" link
test('user cannot see "Manage users" link', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('link', { name: 'Manage users' })).not.toBeVisible();
});