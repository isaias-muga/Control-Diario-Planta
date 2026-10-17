import { expect, test, type Page } from '@playwright/test';
import { crearProveedor, irAConfiguracion, loginYEsperarResumen } from '../helpers';

test.use({ viewport: { width: 390, height: 844 } });

async function sinScrollHorizontal(page: Page): Promise<void> {
  const medidas = await page.evaluate(() => ({
    contenido: document.documentElement.scrollWidth,
    ventana: document.documentElement.clientWidth,
  }));
  expect(medidas.contenido).toBeLessThanOrEqual(medidas.ventana);
}

test.beforeEach(async ({ page }) => {
  await loginYEsperarResumen(page);
});

test('movil: the three tabs fit the screen and the gear opens Configuración', async ({ page }) => {
  const ventana = page.viewportSize()!.width;
  for (const nombre of ['Resumen', 'Recepciones', 'Proveedores']) {
    const caja = await page.getByRole('link', { name: nombre, exact: true }).boundingBox();
    expect(caja).not.toBeNull();
    expect(caja!.x).toBeGreaterThanOrEqual(0);
    expect(caja!.x + caja!.width).toBeLessThanOrEqual(ventana);
  }
  // Not clipped by the tabs row itself either: it has nothing to scroll at 390px
  const tabs = page.getByRole('navigation', { name: 'Secciones' });
  expect(await tabs.evaluate((e) => e.scrollWidth <= e.clientWidth)).toBe(true);

  const engranaje = page.getByRole('button', { name: 'Ajustes' });
  await expect(engranaje).toBeVisible();
  const caja = await engranaje.boundingBox();
  expect(caja!.x + caja!.width).toBeLessThanOrEqual(ventana);
  await irAConfiguracion(page);

  await expect(page.getByRole('heading', { name: 'Configuración', exact: true })).toBeVisible();
});

test('movil: the page never scrolls horizontally on Resumen, Recepciones, a proveedor and Configuración', async ({ page }) => {
  await sinScrollHorizontal(page);

  await page.getByRole('link', { name: 'Recepciones', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Recepciones', exact: true })).toBeVisible();
  await sinScrollHorizontal(page);

  const nombre = await crearProveedor(page);
  await expect(page.getByRole('heading', { name: nombre })).toBeVisible();
  await sinScrollHorizontal(page);

  await irAConfiguracion(page);
  await expect(page.getByRole('heading', { name: 'Configuración', exact: true })).toBeVisible();
  await sinScrollHorizontal(page);
});

test('movil: on a very narrow screen the tabs row scrolls inside itself, the page does not, and the active tab stays visible', async ({ page }) => {
  await page.setViewportSize({ width: 280, height: 700 });
  await page.getByRole('link', { name: 'Proveedores', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Proveedores', exact: true })).toBeVisible();

  await sinScrollHorizontal(page);
  const tabs = page.getByRole('navigation', { name: 'Secciones' });
  expect(await tabs.evaluate((e) => e.scrollWidth > e.clientWidth)).toBe(true);
  await expect(async () => {
    const dentro = await page.getByRole('link', { name: 'Proveedores', exact: true }).evaluate((a) => {
      const caja = a.getBoundingClientRect();
      const nav = a.closest('nav')!.getBoundingClientRect();
      return caja.left >= nav.left - 1 && caja.right <= nav.right + 1;
    });
    expect(dentro).toBe(true);
  }).toPass({ timeout: 5_000 });
});
