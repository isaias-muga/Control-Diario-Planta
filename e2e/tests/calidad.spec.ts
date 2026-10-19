import { expect, test, type Page } from '../fixtures';
import {
  cambiarAcidezMaximaPorApi,
  crearProveedor,
  guardarLimites,
  irAConfiguracion,
  loginYEsperarInicio,
  registrarRecepcion,
  rellenar,
  restaurarAcidezMaxima,
} from '../helpers';

// The limits row is global: the tests that change it always restore the default (restaurarAcidezMaxima).

async function fueraDeRangoEn(page: Page, nombre: string): Promise<string> {
  await page.getByRole('link', { name: 'Resumen' }).click();
  const fila = page.getByRole('row', { name: new RegExp(nombre) });
  await expect(fila).toBeVisible();
  return (await fila.getByRole('cell').last().innerText()).trim();
}

test.beforeEach(async ({ page }) => {
  await loginYEsperarInicio(page);
});

test('calidad: a value outside the limits is saved and flagged as out of range', async ({ page }) => {
  const nombre = await crearProveedor(page);

  // Acidity 25 is plausible (10-30) but above the default maximum of 19.
  await registrarRecepcion(page, { litros: 1000, acidez: 25, temperatura: 5 });
  await expect(page.getByText('1 de 1 recepciones')).toBeVisible();

  expect(await fueraDeRangoEn(page, nombre)).toBe('1');
});

// demoraMs > 0 holds the save request back, like a slow connection: Resumen must still see the new limit.
for (const demoraMs of [0, 500]) {
  const titulo = 'calidad: changing a limit in Configuración changes what is flagged';
  test(demoraMs ? `${titulo} (slow save)` : titulo, async ({ page, request }) => {
    if (demoraMs) {
      await page.route('**/api/Configuracion/calidad', async (route) => {
        if (route.request().method() === 'PUT') {
          await new Promise((resolve) => setTimeout(resolve, demoraMs));
        }
        await route.continue();
      });
    }
    const nombre = await crearProveedor(page);
    await registrarRecepcion(page, { litros: 1000, acidez: 22, temperatura: 5 });
    await expect(page.getByText('1 de 1 recepciones')).toBeVisible();
    expect(await fueraDeRangoEn(page, nombre)).toBe('1');

    await irAConfiguracion(page);
    await expect(page.getByLabel(/^Acidez máxima/)).toBeVisible();

    try {
      await rellenar(page.getByLabel(/^Acidez máxima/), '25');
      await guardarLimites(page);

      expect(await fueraDeRangoEn(page, nombre)).toBe('0');
    } finally {
      // Always restore the documented default through the API: it works even after a timeout or a closed page.
      await restaurarAcidezMaxima(request);
    }
  });
}

test('calidad: a limit typed as "19,5" is saved as 19.5 and shown as 19,5 after reloading', async ({ page, request }) => {
  await irAConfiguracion(page);
  await expect(page.getByLabel(/^Acidez máxima/)).toBeVisible();

  try {
    await rellenar(page.getByLabel(/^Acidez máxima/), '19,5');
    await guardarLimites(page);

    await page.reload();
    await expect(page.getByLabel(/^Acidez máxima/)).toHaveValue('19,5');
  } finally {
    await restaurarAcidezMaxima(request);
  }
});

test('calidad: saving limits that another user changed meanwhile shows the conflict and keeps what was typed', async ({ page, request }) => {
  await irAConfiguracion(page);
  await expect(page.getByLabel(/^Acidez máxima/)).toHaveValue('19');

  try {
    // Another user saves while this page is open: the page's version is now stale
    await cambiarAcidezMaximaPorApi(request, 22);
    await rellenar(page.getByLabel(/^Acidez máxima/), '25');
    await page.getByRole('button', { name: 'Guardar' }).click();

    await expect(page.getByRole('alert').filter({ hasText: 'Los límites fueron modificados por otro usuario. Recargá la página.' })).toBeVisible();
    await expect(page.getByLabel(/^Acidez máxima/)).toHaveValue('25');

    // Reloading shows what the other user saved, and saving from there works
    await page.reload();
    await expect(page.getByLabel(/^Acidez máxima/)).toHaveValue('22');
  } finally {
    await restaurarAcidezMaxima(request);
  }
});

test('calidad: the API rejects a limits update without version (400) and with a stale one (409)', async ({ request }) => {
  const login = await request.post('/api/Auth/login', { data: { username: 'admin', password: process.env.E2E_ADMIN_PASSWORD ?? process.env.ADMIN_PASSWORD } });
  expect(login.status()).toBe(200);
  const headers = { Authorization: `Bearer ${(await login.json()).token}` };
  const actuales = await (await request.get('/api/Configuracion/calidad', { headers })).json();
  const limites = { acidezMaxima: 19, temperaturaMaxima: actuales.temperaturaMaxima, aguaMaxima: actuales.aguaMaxima, cantidadSilos: actuales.cantidadSilos };

  try {
    const sinVersion = await request.put('/api/Configuracion/calidad', { headers, data: limites });
    expect(sinVersion.status()).toBe(400);
    expect(await sinVersion.json()).toEqual({ error: 'Falta la versión de los límites. Recargá la página.', field: 'version' });

    await cambiarAcidezMaximaPorApi(request, 21);
    const obsoleta = await request.put('/api/Configuracion/calidad', { headers, data: { ...limites, version: actuales.version } });
    expect(obsoleta.status()).toBe(409);
    expect(await obsoleta.json()).toEqual({ error: 'Los límites fueron modificados por otro usuario. Recargá la página.' });
  } finally {
    await restaurarAcidezMaxima(request);
  }
});

test('calidad: a double click on Guardar saves once and shows no conflict', async ({ page, request }) => {
  await irAConfiguracion(page);
  await expect(page.getByLabel(/^Acidez máxima/)).toHaveValue('19');
  const guardados: string[] = [];
  page.on('request', (r) => {
    if (r.method() === 'PUT' && r.url().endsWith('/api/Configuracion/calidad')) {
      guardados.push(r.url());
    }
  });

  try {
    await rellenar(page.getByLabel(/^Acidez máxima/), '20');
    await page.getByRole('button', { name: 'Guardar' }).dblclick();

    await expect(page.getByText('Configuración guardada.').first()).toBeVisible();
    await expect(page.getByText('Los límites fueron modificados por otro usuario.')).toHaveCount(0);
    expect(guardados).toHaveLength(1);
  } finally {
    await restaurarAcidezMaxima(request);
  }
});

test('calidad: a session rejected by the API while saving sends the user to the login page', async ({ page, request }) => {
  await irAConfiguracion(page);
  await expect(page.getByLabel(/^Acidez máxima/)).toHaveValue('19');
  await page.route('**/api/Configuracion/calidad', (route) =>
    route.request().method() === 'PUT' ? route.fulfill({ status: 401, body: '' }) : route.continue(),
  );

  try {
    await rellenar(page.getByLabel(/^Acidez máxima/), '20');
    await page.getByRole('button', { name: 'Guardar' }).click();

    await expect(page).toHaveURL(/\/login/);
  } finally {
    await restaurarAcidezMaxima(request);
  }
});
