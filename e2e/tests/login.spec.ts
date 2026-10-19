import { expect, test } from '../fixtures';
import { login, loginYEsperarInicio } from '../helpers';

test('login: valid credentials open the Recepciones page', async ({ page }) => {
  await loginYEsperarInicio(page);
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
