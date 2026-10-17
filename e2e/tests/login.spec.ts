import { expect, test } from '../fixtures';
import { login, loginYEsperarResumen } from '../helpers';

test('login: valid credentials open the Resumen page', async ({ page }) => {
  await loginYEsperarResumen(page);
});

test('login: wrong password shows an error and stays on the login page', async ({ page }) => {
  await login(page, 'definitely-not-the-password');

  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page).toHaveURL(/\/login$/);
});

test('login: a protected page without a session redirects to login', async ({ page }) => {
  await page.goto('/proveedores');

  await expect(page).toHaveURL(/\/login/);
});
