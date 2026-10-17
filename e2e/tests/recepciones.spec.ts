import { expect, test } from '../fixtures';
import {
  abrirRegistro,
  confirmarRecepcion,
  crearProveedor,
  llenarRecepcion,
  loginYEsperarResumen,
  registrarRecepcion,
  rellenar,
  revisarDatos,
  ticketUnico,
} from '../helpers';

test.beforeEach(async ({ page }) => {
  await loginYEsperarResumen(page);
});

test('recepción: a valid one is saved and listed with its ticket', async ({ page }) => {
  await crearProveedor(page);
  const ticket = ticketUnico();

  await registrarRecepcion(page, { litros: 1500, acidez: 16, temperatura: 5, ticket });

  await expect(page.getByRole('cell', { name: String(ticket), exact: true })).toBeVisible();
  await expect(page.getByText('1 de 1 recepciones')).toBeVisible();
});

test('recepción: an impossible value shows its message and is not saved', async ({ page }) => {
  await crearProveedor(page);
  const paginaDelProveedor = page.url();
  await abrirRegistro(page);

  await llenarRecepcion(page, { litros: 1500, acidez: 5, temperatura: 5 });
  await revisarDatos(page);

  await expect(page.getByText('Requerido, entre 10 y 30 °D.')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Nueva recepción' })).toBeVisible();
  await page.goto(paginaDelProveedor);
  await expect(page.getByText('No hay recepciones con estos filtros.')).toBeVisible();
});

test('recepción: a duplicate ticket shows its message and is not saved', async ({ page }) => {
  const ticket = ticketUnico();
  await crearProveedor(page);
  await registrarRecepcion(page, { litros: 1000, acidez: 16, temperatura: 5, ticket });
  await expect(page.getByRole('cell', { name: String(ticket), exact: true })).toBeVisible();

  await crearProveedor(page);
  const paginaDelProveedor = page.url();
  await abrirRegistro(page);
  await llenarRecepcion(page, { litros: 1000, acidez: 16, temperatura: 5, ticket });
  await revisarDatos(page);
  await page.getByRole('button', { name: 'Confirmar' }).click();

  await expect(page.getByText('Ese ticket ya fue cargado. Consultá en la oficina.')).toBeVisible();
  await page.goto(paginaDelProveedor);
  await expect(page.getByText('No hay recepciones con estos filtros.')).toBeVisible();
});

test('recepción: a double click on Confirmar saves only one', async ({ page }) => {
  await crearProveedor(page);
  const paginaDelProveedor = page.url();
  // Slow response so the second click lands while the first request is in flight
  await page.route('**/api/Proveedores/*/recepciones', async (route) => {
    await new Promise((resolve) => setTimeout(resolve, 1000));
    await route.continue();
  });
  await abrirRegistro(page);
  await llenarRecepcion(page, { litros: 1000, acidez: 16, temperatura: 5 });
  await revisarDatos(page);

  await page.getByRole('button', { name: 'Confirmar' }).dblclick();

  await expect(page.getByRole('heading', { name: 'Recepción guardada' })).toBeVisible();
  await page.goto(paginaDelProveedor);
  await expect(page.getByText('1 de 1 recepciones')).toBeVisible();
});

test('recepción: litros typed as "1.234,5" are saved as 1234.5 and shown as 1.234,5', async ({ page }) => {
  await crearProveedor(page);

  await registrarRecepcion(page, { litros: '1.234,5', acidez: '17,5', temperatura: 5 });

  // Never 1,2345: the list shows the saved liters with es-AR separators
  await expect(page.getByRole('cell', { name: '1.234,5', exact: true }).first()).toBeVisible();
  await expect(page.getByText('1 de 1 recepciones')).toBeVisible();
});

test('recepción: an ambiguous number shows its message and is not saved', async ({ page }) => {
  await crearProveedor(page);
  const paginaDelProveedor = page.url();
  await abrirRegistro(page);

  await llenarRecepcion(page, { litros: '1.2.3', acidez: 16, temperatura: 5 });
  await revisarDatos(page);

  await expect(page.getByText('Usá punto para los miles y coma para los decimales (1.234,5).')).toBeVisible();
  await page.goto(paginaDelProveedor);
  await expect(page.getByText('No hay recepciones con estos filtros.')).toBeVisible();
});

test('recepción: a decimal ticket is rejected with the integer message', async ({ page }) => {
  await crearProveedor(page);
  await abrirRegistro(page);

  await rellenar(page.getByLabel(/^Ticket/), '12,5');
  await page.getByLabel(/^Litros/).click(); // leave the field so its error shows

  await expect(page.getByText('Ingresá un número entero, sin decimales.')).toBeVisible();
});

test('recepción: a recepción without a silo is not accepted and nothing is saved', async ({ page }) => {
  await crearProveedor(page);
  const paginaDelProveedor = page.url();
  await abrirRegistro(page);
  await rellenar(page.getByLabel(/^Litros/), '1000');
  await rellenar(page.getByLabel(/^Acidez/), '16');
  await rellenar(page.getByLabel(/^Temp/), '5');
  await rellenar(page.getByLabel(/^Ticket/), String(ticketUnico()));

  await revisarDatos(page);

  await expect(page.getByText('Elegí el silo.')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Nueva recepción' })).toBeVisible();
  await page.goto(paginaDelProveedor);
  await expect(page.getByText('No hay recepciones con estos filtros.')).toBeVisible();
});

test('recepción: the API refuses a missing silo and a silo above the number of silos (400 on silo), saving nothing', async ({ page }) => {
  await crearProveedor(page);
  const id = page.url().split('/').pop();
  const token = await page.evaluate(() => localStorage.getItem('dairycontrol_token'));
  const headers = { Authorization: `Bearer ${token}` };
  const hoy = new Intl.DateTimeFormat('sv-SE', { timeZone: 'America/Argentina/Buenos_Aires', dateStyle: 'short', timeStyle: 'medium' }).format(new Date()).replace(' ', 'T');
  const base = { litros: 1000, acidez: 16, temperatura: 5, fechaHora: hoy };

  const casos: { silo?: number; mensaje: string }[] = [
    { mensaje: 'El silo es obligatorio.' },
    { silo: 4, mensaje: 'El silo debe estar entre 1 y 3.' },
    { silo: 0, mensaje: 'El silo debe estar entre 1 y 3.' },
    { silo: 21, mensaje: 'El silo debe estar entre 1 y 3.' },
  ];
  for (const caso of casos) {
    const respuesta = await page.request.post(`/api/Proveedores/${id}/recepciones`, { headers, data: { ...base, silo: caso.silo, ticket: ticketUnico() } });
    const cuerpo = await respuesta.json();

    expect(respuesta.status()).toBe(400);
    expect(cuerpo.field).toBe('silo');
    expect(cuerpo.error).toBe(caso.mensaje);
  }
  await page.reload();
  await expect(page.getByText('No hay recepciones con estos filtros.')).toBeVisible();
});
