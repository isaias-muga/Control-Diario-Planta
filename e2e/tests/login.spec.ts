import { expect, test } from '../fixtures';
import { login, loginYEsperarInicio } from '../helpers';

test('login: valid credentials open the Recepciones page on the Hoy filter', async ({ page }) => {
  await loginYEsperarInicio(page);

  await expect(page).toHaveURL(/\/recepciones$/);
  await expect(page.getByRole('radio', { name: 'Hoy' })).toBeChecked();
});

test('the web manifest carries the app name and is served as a manifest', async ({ request }) => {
  const respuesta = await request.get('/manifest.webmanifest');

  expect(respuesta.status()).toBe(200);
  expect(respuesta.headers()['content-type']).toContain('application/manifest+json');
  const manifiesto = await respuesta.json();
  expect(manifiesto.name).toBe('Control Diario Fábrica');
  expect(manifiesto.short_name).toBe('Control Diario');
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
