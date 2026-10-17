import { expect, test, type Page } from '../fixtures';
import { crearProveedor, loginYEsperarResumen, registrarRecepcion, rellenar, ticketUnico } from '../helpers';

const ZONA_PLANTA = 'America/Argentina/Buenos_Aires';

/** Today in plant time as YYYY-MM-DD, computed in the test independently of the app. */
function hoyEnPlanta(): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: ZONA_PLANTA }).format(new Date());
}

async function abrirRecepciones(page: Page): Promise<void> {
  await page.getByRole('link', { name: 'Recepciones', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Recepciones', exact: true })).toBeVisible();
}

/** Opens "Registrar recepción" on the Recepciones page and chooses the proveedor in the form. */
async function abrirRegistroConProveedor(page: Page, nombre: string) {
  await page.getByRole('button', { name: '+ Registrar recepción' }).click();
  const dialog = page.getByRole('dialog');
  const selector = dialog.getByRole('combobox', { name: /Proveedor/ });
  await expect(selector).toBeFocused(); // the dialog's initial focus: wait for it before touching the form
  await selector.click();
  await page.getByRole('option', { name: nombre }).click();
  return dialog;
}

async function completarLectura(dialog: ReturnType<Page['getByRole']>, litros: number): Promise<void> {
  await rellenar(dialog.getByLabel(/^Litros/), String(litros));
  await rellenar(dialog.getByLabel(/^Acidez/), '16');
  await rellenar(dialog.getByLabel(/^Temperatura/), '5');
}

test.beforeEach(async ({ page }) => {
  await loginYEsperarResumen(page);
});

test('recepciones: the list shows a recepción registered from the page, with the proveedor chosen in the form', async ({ page }) => {
  const nombre = await crearProveedor(page);
  const ticket = ticketUnico();
  await abrirRecepciones(page);

  const dialog = await abrirRegistroConProveedor(page, nombre);
  await completarLectura(dialog, 2150);
  await rellenar(dialog.getByLabel(/^Ticket/), String(ticket));
  await expect(dialog.getByText('Litros reales:')).toContainText('2.150');
  await dialog.getByRole('button', { name: 'Registrar', exact: true }).click();

  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByRole('cell', { name: String(ticket), exact: true })).toBeVisible();
  const fila = page.getByRole('row', { name: new RegExp(`${ticket}.*${nombre}`) });
  await expect(fila.getByRole('link', { name: nombre })).toBeVisible();
  await expect(fila.getByText('OK', { exact: true })).toBeVisible();
});

test('recepciones: "Registrar y cargar otra" registers two with consecutive suggested tickets', async ({ page }) => {
  const nombre = await crearProveedor(page);
  await abrirRecepciones(page);

  const dialog = await abrirRegistroConProveedor(page, nombre);
  const campoTicket = dialog.getByLabel(/^Ticket/);
  await expect(dialog.getByText('Siguiente número sugerido; se puede cambiar.')).toBeVisible();
  const primero = Number(await campoTicket.inputValue());
  expect(primero).toBeGreaterThan(0);

  await completarLectura(dialog, 1000);
  await dialog.getByRole('button', { name: 'Registrar y cargar otra' }).click();

  // The dialog stays open, the lectura is cleared and the next ticket is suggested
  await expect(campoTicket).toHaveValue(String(primero + 1));
  await expect(dialog.getByLabel(/^Litros/)).toHaveValue('');
  await expect(dialog.getByLabel(/^Litros/)).toBeFocused(); // ready for the next lectura
  await expect(page.getByRole('dialog')).toHaveCount(1);
  await completarLectura(dialog, 1200);
  await dialog.getByRole('button', { name: 'Registrar', exact: true }).click();

  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByRole('cell', { name: String(primero), exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: String(primero + 1), exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: nombre })).toHaveCount(2);
});

test('recepciones: an out-of-range recepción shows its reason and the "Solo fuera de rango" filter finds it', async ({ page }) => {
  const nombre = await crearProveedor(page);
  const ticket = ticketUnico();
  await registrarRecepcion(page, { litros: 1000, acidez: 25, temperatura: 5, ticket });
  await expect(page.getByRole('cell', { name: String(ticket), exact: true })).toBeVisible();
  await abrirRecepciones(page);

  await page.getByRole('textbox', { name: 'Ticket' }).fill(String(ticket));
  await page.getByRole('checkbox', { name: 'Solo fuera de rango' }).check();

  const fila = page.getByRole('row', { name: new RegExp(String(ticket)) });
  await expect(fila.getByText('Acidez alta')).toBeVisible();
  await expect(page).toHaveURL(new RegExp(`ticket=${ticket}.*fuera=1|fuera=1.*ticket=${ticket}`));
  await expect(page.getByText('1 de 1 recepciones')).toBeVisible();
  await expect(page.getByRole('link', { name: nombre })).toBeVisible();
});

test('recepciones: a Resumen link opens the list filtered by day or by proveedor', async ({ page }) => {
  const nombre = await crearProveedor(page);
  const ticket = ticketUnico();
  await registrarRecepcion(page, { litros: 1500, acidez: 16, temperatura: 5, ticket });
  await expect(page.getByRole('cell', { name: String(ticket), exact: true })).toBeVisible();

  // The ranking row of the proveedor opens the list for that proveedor and the period of the Resumen
  await page.getByRole('link', { name: 'Resumen' }).click();
  await page.getByRole('link', { name: nombre }).click();

  await expect(page.getByRole('heading', { name: 'Recepciones', exact: true })).toBeVisible();
  await expect(page.getByText(nombre).first()).toBeVisible();
  await expect(page).toHaveURL(/proveedor=/);
  await expect(page.getByRole('cell', { name: String(ticket), exact: true })).toBeVisible();
  await expect(page.getByText('1 de 1 recepciones')).toBeVisible();

  // The bar of today opens the list for that day
  await page.getByRole('link', { name: 'Resumen' }).click();
  const hoy = hoyEnPlanta();
  const [anio, mes, dia] = hoy.split('-');
  await page.getByRole('link', { name: new RegExp(`^Ver recepciones\\. ${dia}/${mes}/${anio}`) }).click();

  await expect(page).toHaveURL(new RegExp(`desde=${hoy}&hasta=${hoy}`));
  await expect(page.getByRole('cell', { name: String(ticket), exact: true })).toBeVisible();
});

test('recepciones: the filters stay in the URL, so reloading keeps them', async ({ page }) => {
  const ticket = ticketUnico();
  await crearProveedor(page);
  await registrarRecepcion(page, { litros: 1000, acidez: 16, temperatura: 5, ticket });
  await expect(page.getByRole('cell', { name: String(ticket), exact: true })).toBeVisible();
  await abrirRecepciones(page);

  await page.getByRole('textbox', { name: 'Ticket' }).fill(String(ticket));
  await expect(page).toHaveURL(new RegExp(`ticket=${ticket}`));
  await page.reload();

  await expect(page.getByRole('textbox', { name: 'Ticket' })).toHaveValue(String(ticket));
  await expect(page.getByRole('cell', { name: String(ticket), exact: true })).toBeVisible();
  await expect(page.getByText('1 de 1 recepciones')).toBeVisible();
});

test.describe('a 1366px screen', () => {
  test.use({ viewport: { width: 1366, height: 900 } });

  test('recepciones: the table fits its card without horizontal scroll (the list page and the proveedor page)', async ({ page }) => {
    const ticket = ticketUnico();
    await crearProveedor(page);
    await registrarRecepcion(page, { litros: 1000, acidez: 16, temperatura: 5, ticket });
    await expect(page.getByRole('cell', { name: String(ticket), exact: true })).toBeVisible();

    const sinScroll = () =>
      page.getByRole('table').evaluate((tabla) => {
        const contenedor = tabla.parentElement as HTMLElement;
        return contenedor.scrollWidth <= contenedor.clientWidth;
      });
    expect(await sinScroll()).toBe(true);

    await abrirRecepciones(page);
    await expect(page.getByRole('columnheader', { name: 'Hora de la recepción' })).toBeVisible();
    await expect(page.getByRole('cell', { name: String(ticket), exact: true })).toBeVisible();
    expect(await sinScroll()).toBe(true);
  });
});

test('recepciones: the ticket search reads "1.234" as ticket 1234 and rejects a decimal', async ({ page }) => {
  await abrirRecepciones(page);
  const buscador = page.getByRole('textbox', { name: 'Ticket' });

  await rellenar(buscador, '1.234');
  await expect(page).toHaveURL(/ticket=1234(&|$)/);

  await rellenar(buscador, '12,5');
  await expect(page.getByText('Ingresá un número entero, sin decimales.')).toBeVisible();
});
