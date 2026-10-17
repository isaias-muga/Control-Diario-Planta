import { expect, test, type Page } from '@playwright/test';
import { crearProveedor, loginYEsperarResumen, rellenar, ticketUnico } from '../helpers';

test.use({ viewport: { width: 390, height: 844 } });

test.beforeEach(async ({ page }) => {
  await loginYEsperarResumen(page);
});

interface Carga {
  proveedor: string;
  ticket: number;
  litros?: string;
  acidez?: string;
  temperatura?: string;
}

async function sinScrollHorizontal(page: Page): Promise<void> {
  const medidas = await page.evaluate(() => ({
    contenido: document.documentElement.scrollWidth,
    ventana: document.documentElement.clientWidth,
  }));
  expect(medidas.contenido).toBeLessThanOrEqual(medidas.ventana);
}

/** Opens the quick flow from the Recepciones page. */
async function abrirCargaRapida(page: Page): Promise<void> {
  await page.getByRole('link', { name: 'Recepciones', exact: true }).click();
  await page.getByRole('link', { name: 'Carga rápida' }).click();
  await expect(page.getByRole('heading', { name: 'Nueva recepción' })).toBeVisible();
}

/** Fills step 1 (the proveedor is chosen from the list; the ticket replaces the suggested one). */
async function llenar(page: Page, c: Carga): Promise<void> {
  await page.getByRole('combobox', { name: /Proveedor/ }).click();
  await page.getByRole('option', { name: c.proveedor }).click();
  await rellenar(page.getByLabel(/^Ticket/), String(c.ticket));
  await rellenar(page.getByLabel(/^Litros/), c.litros ?? '1500');
  await rellenar(page.getByLabel(/^Acidez/), c.acidez ?? '16');
  await rellenar(page.getByLabel(/^Temp/), c.temperatura ?? '5');
}

async function nuevaCarga(page: Page, c: Partial<Carga> = {}): Promise<Carga> {
  const proveedor = await crearProveedor(page);
  const carga = { proveedor, ticket: ticketUnico(), ...c };
  await abrirCargaRapida(page);
  await llenar(page, carga);
  return carga;
}

test('carga rápida: registers through the three steps at 390px and the recepción is in the list', async ({ page }) => {
  const carga = await nuevaCarga(page, { litros: '1.234,5' });
  await sinScrollHorizontal(page);

  await page.getByRole('button', { name: 'Revisar datos' }).click();
  await expect(page.getByRole('heading', { name: '¿Son correctos estos datos?' })).toBeVisible();
  await expect(page.getByText(carga.proveedor)).toBeVisible();
  await expect(page.getByText('1.234,5').first()).toBeVisible();
  await sinScrollHorizontal(page);

  await page.getByRole('button', { name: 'Confirmar' }).click();
  await expect(page.getByRole('heading', { name: 'Recepción guardada' })).toBeVisible();
  await expect(page.getByText(new RegExp(`Ticket ${carga.ticket} · `))).toBeVisible();
  await sinScrollHorizontal(page);

  await page.getByRole('link', { name: 'Ir a Recepciones' }).click();
  await expect(page.getByRole('cell', { name: String(carga.ticket), exact: true })).toBeVisible();
});

test('carga rápida: Nueva recepción starts an empty form', async ({ page }) => {
  await nuevaCarga(page);
  await page.getByRole('button', { name: 'Revisar datos' }).click();
  await page.getByRole('button', { name: 'Confirmar' }).click();
  await expect(page.getByRole('heading', { name: 'Recepción guardada' })).toBeVisible();

  await page.getByRole('button', { name: 'Nueva recepción' }).click();

  await expect(page.getByRole('heading', { name: 'Nueva recepción' })).toBeVisible();
  await expect(page.getByLabel(/^Litros/)).toHaveValue('');
  await expect(page.getByLabel(/^Acidez/)).toHaveValue('');
});

test('carga rápida: Corregir goes back to step 1 with the values kept', async ({ page }) => {
  const carga = await nuevaCarga(page, { litros: '2000', acidez: '17,5' });
  await page.getByRole('button', { name: 'Revisar datos' }).click();
  await expect(page.getByRole('heading', { name: '¿Son correctos estos datos?' })).toBeVisible();

  await page.getByRole('button', { name: 'Corregir' }).click();

  await expect(page.getByRole('heading', { name: 'Nueva recepción' })).toBeVisible();
  await expect(page.getByLabel(/^Ticket/)).toHaveValue(String(carga.ticket));
  await expect(page.getByLabel(/^Litros/)).toHaveValue('2.000'); // shown the es-AR way after the field lost focus
  await expect(page.getByLabel(/^Acidez/)).toHaveValue('17,5');
  await expect(page.getByRole('combobox', { name: /Proveedor/ })).toContainText(carga.proveedor);
});

test('carga rápida: the ticket is required', async ({ page }) => {
  await nuevaCarga(page);
  await page.getByLabel(/^Ticket/).fill('');

  await page.getByRole('button', { name: 'Revisar datos' }).click();

  await expect(page.getByText('Requerido. Entero entre 1 y 2.147.483.647.')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Nueva recepción' })).toBeVisible();
});

test('carga rápida: a duplicate ticket shows the office message and returns to step 1 with the values kept', async ({ page }) => {
  const primera = await nuevaCarga(page);
  await page.getByRole('button', { name: 'Revisar datos' }).click();
  await page.getByRole('button', { name: 'Confirmar' }).click();
  await expect(page.getByRole('heading', { name: 'Recepción guardada' })).toBeVisible();

  const segunda = await nuevaCarga(page, { ticket: primera.ticket, litros: '777' });
  await page.getByRole('button', { name: 'Revisar datos' }).click();
  await page.getByRole('button', { name: 'Confirmar' }).click();

  await expect(page.getByText('Ese ticket ya fue cargado. Consultá en la oficina.')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Nueva recepción' })).toBeVisible();
  await expect(page.getByLabel(/^Litros/)).toHaveValue('777');
  await expect(page.getByLabel(/^Ticket/)).toHaveValue(String(segunda.ticket));
});

test('carga rápida: a double tap on Confirmar sends one request', async ({ page }) => {
  await nuevaCarga(page);
  let envios = 0;
  await page.route('**/api/Proveedores/*/recepciones', async (route) => {
    envios += 1;
    // Slow response so the second tap lands while the first request is in flight
    await new Promise((resolve) => setTimeout(resolve, 1000));
    await route.continue();
  });
  await page.getByRole('button', { name: 'Revisar datos' }).click();

  await page.getByRole('button', { name: 'Confirmar' }).dblclick();

  await expect(page.getByRole('heading', { name: 'Recepción guardada' })).toBeVisible();
  expect(envios).toBe(1);
});

test('carga rápida: an acidez over the limit is highlighted on the confirmation with the limit', async ({ page }) => {
  await nuevaCarga(page, { acidez: '25' });

  await page.getByRole('button', { name: 'Revisar datos' }).click();

  await expect(page.getByRole('heading', { name: '¿Son correctos estos datos?' })).toBeVisible();
  await expect(page.getByText(/Fuera de rango \(máx\. 19\)/)).toBeVisible();
  // Only the acidez is over its limit
  await expect(page.getByText(/Fuera de rango/)).toHaveCount(1);
});

test('carga rápida: a network error shows "No se guardó" and Reintentar saves it', async ({ page }) => {
  const carga = await nuevaCarga(page);
  await page.getByRole('button', { name: 'Revisar datos' }).click();
  let fallos = 1;
  await page.route('**/api/Proveedores/*/recepciones', async (route) => {
    if (fallos-- > 0) {
      await route.abort('failed'); // never reaches the server
      return;
    }
    await route.continue();
  });

  await page.getByRole('button', { name: 'Confirmar' }).click();
  await expect(page.getByText('No se guardó')).toBeVisible();
  await page.getByRole('button', { name: 'Reintentar' }).click();

  await expect(page.getByRole('heading', { name: 'Recepción guardada' })).toBeVisible();
  await expect(page.getByText(new RegExp(`Ticket ${carga.ticket} · `))).toBeVisible();
});

test('carga rápida: a retry that gets 409 says it was probably saved', async ({ page }) => {
  await nuevaCarga(page);
  await page.getByRole('button', { name: 'Revisar datos' }).click();
  let primera = true;
  await page.route('**/api/Proveedores/*/recepciones', async (route) => {
    if (primera) {
      primera = false;
      // The server saves it, but the answer is lost: the operator sees a network error
      await route.fetch();
      await route.abort('failed');
      return;
    }
    await route.continue();
  });

  await page.getByRole('button', { name: 'Confirmar' }).click();
  await expect(page.getByText('No se guardó')).toBeVisible();
  await page.getByRole('button', { name: 'Reintentar' }).click();

  await expect(
    page.getByText('Es probable que ya se haya guardado. Consultá en la oficina antes de cargarla de nuevo.'),
  ).toBeVisible();
  await expect(page.getByText('Ese ticket ya fue cargado')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Reintentar' })).toHaveCount(0);
});

test('carga rápida: the two-column step 1 with every error visible does not scroll horizontally at 390px', async ({ page }) => {
  await abrirCargaRapida(page);
  await page.getByLabel(/^Ticket/).fill('');
  await rellenar(page.getByLabel(/^Grasa/), '99');
  await rellenar(page.getByLabel(/^Silo/), '99');
  await rellenar(page.getByLabel(/^Agua/), '999');

  await page.getByRole('button', { name: 'Revisar datos' }).click();

  await expect(page.getByText('Entero entre 1 y 20.')).toBeVisible();
  await expect(page.getByText('Requerido. Entero entre 1 y 2.147.483.647.')).toBeVisible();
  await sinScrollHorizontal(page);
});

test('carga rápida: step 1 fits the 1366px desktop without horizontal scroll', async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 768 });
  await nuevaCarga(page);

  await sinScrollHorizontal(page);
});
