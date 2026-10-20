import { expect, test, type Page } from '../fixtures';
import {
  PASSWORD_PROPIA,
  ahoraEnPlantaMenos,
  cabecerasAutenticadas,
  crearProveedorConIdPorApi,
  crearUsuarioListoPorApi,
  loginComo,
  loginYEsperarInicio,
  registrarRecepcionPorApi,
  rellenar,
  ticketUnico,
} from '../helpers';

// Corrections with a reason and a history, voids instead of deletes, and deactivation: data is never deleted.

/** The proveedor's own page lists its recepciones of the month: the quickest way to a row of a recepción made through the API. */
async function abrirProveedor(page: Page, id: string, nombre: string): Promise<void> {
  await page.goto(`/proveedores/${id}`);
  await expect(page.getByRole('heading', { name: nombre })).toBeVisible();
}

function fila(page: Page, ticket: number) {
  return page.getByRole('row', { name: new RegExp(String(ticket)) });
}

async function abrirMenuDeFila(page: Page, ticket: number): Promise<void> {
  await expect(page.getByRole('cell', { name: String(ticket), exact: true })).toBeVisible();
  await fila(page, ticket).getByRole('button', { name: /^Acciones de la recepción/ }).click();
}

test.beforeEach(async ({ page }) => {
  await loginYEsperarInicio(page);
});

test('correcciones: correcting litros changes the totals, and the history shows old and new with the reason', async ({ page, request }) => {
  const proveedor = await crearProveedorConIdPorApi(request);
  const { ticket } = await registrarRecepcionPorApi(request, proveedor.id, { litros: 1000 });
  await abrirProveedor(page, proveedor.id, proveedor.nombre);
  await expect(page.getByText('Litros recibidos').locator('..')).toContainText('1.000');

  await abrirMenuDeFila(page, ticket);
  await page.getByRole('menuitem', { name: 'Corregir' }).click();
  await expect(page.getByRole('heading', { name: 'Corregir recepción' })).toBeVisible();
  // The form is filled from the recepción once it has loaded
  await expect(page.getByLabel(/^Litros/)).toHaveValue('1.000');
  await expect(page.getByLabel(/^Ticket/)).toHaveValue(String(ticket));

  await rellenar(page.getByLabel(/^Litros/), '1250');
  await rellenar(page.getByLabel('Motivo de la corrección'), 'Error de tipeo en la balanza');
  await page.getByRole('button', { name: 'Revisar cambios' }).click();

  // The confirmation shows old to new, only for what changed
  await expect(page.getByRole('heading', { name: '¿Corregir esta recepción?' })).toBeVisible();
  await expect(page.getByText('1.000', { exact: true })).toBeVisible();
  await expect(page.getByText('1.250', { exact: true })).toBeVisible();
  await expect(page.getByText('Error de tipeo en la balanza')).toBeVisible();
  await page.getByRole('button', { name: 'Confirmar' }).click();
  await expect(page.getByRole('heading', { name: 'Recepción corregida' })).toBeVisible();

  // The totals and the row have the new value, and the row says it was corrected
  await abrirProveedor(page, proveedor.id, proveedor.nombre);
  await expect(page.getByText('Litros recibidos').locator('..')).toContainText('1.250');
  await expect(fila(page, ticket).getByRole('cell', { name: '1.250' }).first()).toBeVisible();
  await page.getByRole('button', { name: 'Corregida 1 vez. Ver historial' }).click();
  const historial = page.getByRole('dialog');
  await expect(historial).toContainText('Motivo: Error de tipeo en la balanza');
  await expect(historial).toContainText('Litros: 1.000 → 1.250');
  await expect(historial.getByText(/\d{2}\/\d{2}\/\d{4} \d{2}:\d{2} · \S+/)).toBeVisible(); // when and who
});

test('correcciones: a ticket that another recepción uses is refused and nothing changes', async ({ page, request }) => {
  const proveedor = await crearProveedorConIdPorApi(request);
  const primera = await registrarRecepcionPorApi(request, proveedor.id, { litros: 1000 });
  const segunda = await registrarRecepcionPorApi(request, proveedor.id, { litros: 2000 });
  await abrirProveedor(page, proveedor.id, proveedor.nombre);

  await abrirMenuDeFila(page, primera.ticket);
  await page.getByRole('menuitem', { name: 'Corregir' }).click();
  await expect(page.getByLabel(/^Ticket/)).toHaveValue(String(primera.ticket));
  await rellenar(page.getByLabel(/^Ticket/), String(segunda.ticket));
  await rellenar(page.getByLabel('Motivo de la corrección'), 'Ticket mal copiado');
  await page.getByRole('button', { name: 'Revisar cambios' }).click();
  await page.getByRole('button', { name: 'Confirmar' }).click();

  await expect(page.getByRole('alert')).toContainText(`El ticket ${segunda.ticket} ya está registrado`);
  await expect(page.getByRole('heading', { name: 'Corregir recepción' })).toBeVisible(); // back on the form
  await abrirProveedor(page, proveedor.id, proveedor.nombre);
  await expect(page.getByRole('cell', { name: String(primera.ticket), exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: /Corregida/ })).toHaveCount(0);
});

test('correcciones: a stale version is refused (409) even when only litros changed, and the second admin does not overwrite the first', async ({ request }) => {
  const proveedor = await crearProveedorConIdPorApi(request);
  const { id } = await registrarRecepcionPorApi(request, proveedor.id, { litros: 1000 });
  const headers = await cabecerasAutenticadas(request);
  const leida = await (await request.get(`/api/Recepciones/${id}`, { headers })).json();
  const correccion = (litros: number, version: string) => ({
    proveedorId: leida.proveedorId, litros, acidez: leida.acidez, temperatura: leida.temperatura, silo: leida.silo, ticket: leida.ticket,
    fechaHora: leida.fechaHora, observaciones: leida.observaciones, motivo: 'Corrección de prueba', version,
  });

  const primera = await request.put(`/api/Recepciones/${id}`, { headers, data: correccion(1100, leida.version) });
  expect(primera.status()).toBe(200);
  const segunda = await request.put(`/api/Recepciones/${id}`, { headers, data: correccion(1300, leida.version) }); // the version both read
  expect(segunda.status()).toBe(409);

  expect((await (await request.get(`/api/Recepciones/${id}`, { headers })).json()).litros).toBe(1100);
  const historial = await (await request.get(`/api/Recepciones/${id}/historial`, { headers })).json();
  expect(historial).toHaveLength(1);
  expect(historial[0].cambios[0]).toMatchObject({ campo: 'litros', valorAnterior: '1000', valorNuevo: '1100' });
});

test('correcciones: a correction needs a reason and a change', async ({ page, request }) => {
  const proveedor = await crearProveedorConIdPorApi(request);
  const { ticket } = await registrarRecepcionPorApi(request, proveedor.id, { litros: 1000 });
  await abrirProveedor(page, proveedor.id, proveedor.nombre);
  await abrirMenuDeFila(page, ticket);
  await page.getByRole('menuitem', { name: 'Corregir' }).click();
  await expect(page.getByLabel(/^Litros/)).toHaveValue('1.000');

  // Nothing changed, with a reason
  await rellenar(page.getByLabel('Motivo de la corrección'), 'Sin cambios en realidad');
  await page.getByRole('button', { name: 'Revisar cambios' }).click();
  await expect(page.getByText('No cambiaste ningún dato.')).toBeVisible();

  // A change without a (long enough) reason
  await rellenar(page.getByLabel(/^Litros/), '1100');
  await rellenar(page.getByLabel('Motivo de la corrección'), 'abc');
  await page.getByRole('button', { name: 'Revisar cambios' }).click();
  await expect(page.getByText('Escribí el motivo (mínimo 5 caracteres).')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Corregir recepción' })).toBeVisible();
});

test('correcciones: voiding keeps the recepción as Anulada, out of the totals, and its ticket stays used', async ({ page, request }) => {
  const proveedor = await crearProveedorConIdPorApi(request);
  const anulada = await registrarRecepcionPorApi(request, proveedor.id, { litros: 1000 });
  const otra = await registrarRecepcionPorApi(request, proveedor.id, { litros: 500 });
  await abrirProveedor(page, proveedor.id, proveedor.nombre);
  await expect(page.getByText('Litros recibidos').locator('..')).toContainText('1.500');

  await abrirMenuDeFila(page, anulada.ticket);
  await page.getByRole('menuitem', { name: 'Anular' }).click();
  const dialogo = page.getByRole('alertdialog');
  await expect(dialogo).toContainText('no se puede volver a usar');
  await rellenar(dialogo.getByLabel('Motivo'), 'Se cargó dos veces');
  await dialogo.getByRole('button', { name: 'Anular' }).click();
  await expect(page.getByText('Recepción anulada.')).toBeVisible();

  // Gone from the list and the totals
  await expect(page.getByRole('cell', { name: String(anulada.ticket), exact: true })).toHaveCount(0);
  await expect(page.getByRole('cell', { name: String(otra.ticket), exact: true })).toBeVisible();
  await expect(page.getByText('Litros recibidos').locator('..')).toContainText('500');
  await expect(page.getByText('Litros recibidos').locator('..')).not.toContainText('1.500');

  // "Incluir anuladas" shows it as Anulada, with its reason, and the totals still leave it out
  await page.getByRole('checkbox', { name: 'Incluir anuladas' }).check();
  await expect(page.getByRole('cell', { name: String(anulada.ticket), exact: true })).toBeVisible();
  await expect(fila(page, anulada.ticket).getByRole('button', { name: 'Anulada' })).toBeVisible();
  await expect(page.getByText('Litros recibidos').locator('..')).not.toContainText('1.500');
  await fila(page, anulada.ticket).getByRole('button', { name: 'Anulada' }).click();
  await expect(page.getByRole('dialog')).toContainText('Motivo: Se cargó dos veces');
  await expect(page.getByRole('dialog')).toContainText('Estado: Vigente → Anulada');
  await page.getByRole('dialog').getByRole('button', { name: 'Cerrar' }).click();

  // Voiding is final: only the history is left in the menu
  await abrirMenuDeFila(page, anulada.ticket);
  await expect(page.getByRole('menuitem')).toHaveText(['Historial']);
  await page.keyboard.press('Escape');

  // The Resumen of the API leaves it out, and its ticket cannot be reused
  const headers = await cabecerasAutenticadas(request);
  const hoy = ahoraEnPlantaMenos(0).slice(0, 10);
  const resumen = await (await request.get(`/api/Recepciones/resumen?desde=${hoy}&hasta=${hoy}&proveedorId=${proveedor.id}`, { headers })).json();
  expect(resumen.totales.litros).toBe(500);
  const reutiliza = await request.post(`/api/Proveedores/${proveedor.id}/recepciones`, {
    headers,
    data: { litros: 300, acidez: 17, temperatura: 5, silo: 1, ticket: anulada.ticket, fechaHora: ahoraEnPlantaMenos(1) },
  });
  expect(reutiliza.status()).toBe(409);
  expect((await reutiliza.json()).error).toContain('anulada');
});

test('correcciones: a deactivated proveedor is not offered for a new recepción, keeps its recepciones and can be reactivated', async ({ page, request }) => {
  const proveedor = await crearProveedorConIdPorApi(request);
  const { ticket } = await registrarRecepcionPorApi(request, proveedor.id, { litros: 800 });
  await abrirProveedor(page, proveedor.id, proveedor.nombre);

  await page.getByRole('button', { name: 'Desactivar' }).click();
  const dialogo = page.getByRole('dialog');
  await expect(dialogo).toContainText('Sus recepciones y su historial se conservan');
  await rellenar(dialogo.getByLabel('Motivo'), 'Dejó de entregar');
  await dialogo.getByRole('button', { name: 'Desactivar' }).click();
  await expect(page.getByRole('heading', { name: `${proveedor.nombre} (inactivo)` })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Registrar recepción' })).toHaveCount(0);
  // Its history of recepciones is still there
  await expect(page.getByRole('cell', { name: String(ticket), exact: true })).toBeVisible();

  // The registration flow does not offer it
  await page.goto('/recepciones/carga');
  await expect(page.getByRole('heading', { name: 'Nueva recepción' })).toBeVisible();
  await expect(page.getByRole('radio', { name: '1', exact: true })).toBeVisible(); // the options have arrived
  await page.getByRole('combobox', { name: /Proveedor/ }).click();
  await expect(page.getByRole('option').first()).toBeVisible();
  await expect(page.getByRole('option', { name: proveedor.nombre })).toHaveCount(0);
  await page.keyboard.press('Escape');

  // The API refuses a recepción for it, and the list of proveedores marks it
  const headers = await cabecerasAutenticadas(request);
  const registro = await request.post(`/api/Proveedores/${proveedor.id}/recepciones`, {
    headers,
    data: { litros: 100, acidez: 17, temperatura: 5, silo: 1, ticket: ticketUnico(), fechaHora: ahoraEnPlantaMenos(1) },
  });
  expect(registro.status()).toBe(400);
  await page.goto('/proveedores');
  await expect(page.getByRole('link', { name: new RegExp(`${proveedor.nombre}.*inactivo`) })).toBeVisible();

  // Reactivating offers it again
  await abrirProveedor(page, proveedor.id, `${proveedor.nombre} (inactivo)`);
  await page.getByRole('button', { name: 'Reactivar' }).click();
  await rellenar(page.getByRole('dialog').getByLabel('Motivo'), 'Volvió a entregar');
  await page.getByRole('dialog').getByRole('button', { name: 'Reactivar' }).click();
  await expect(page.getByRole('link', { name: 'Registrar recepción' })).toBeVisible();
});

test('correcciones: correcting the name of a proveedor needs a reason and the old name stays in its history', async ({ page, request }) => {
  const proveedor = await crearProveedorConIdPorApi(request);
  await abrirProveedor(page, proveedor.id, proveedor.nombre);

  await page.getByRole('button', { name: 'Corregir nombre' }).click();
  const dialogo = page.getByRole('dialog');
  await rellenar(dialogo.getByLabel('Nombre'), `${proveedor.nombre} S.A.`);
  await dialogo.getByRole('button', { name: 'Corregir' }).click(); // without a reason
  await expect(dialogo).toContainText('Escribí el motivo (mínimo 5 caracteres).');
  await rellenar(dialogo.getByLabel('Motivo'), 'Nombre mal escrito');
  await dialogo.getByRole('button', { name: 'Corregir' }).click();

  await expect(page.getByRole('heading', { name: `${proveedor.nombre} S.A.` })).toBeVisible();
  await page.getByRole('button', { name: 'Historial' }).click();
  await expect(page.getByRole('dialog')).toContainText('Motivo: Nombre mal escrito');
  await expect(page.getByRole('dialog')).toContainText(`Nombre: ${proveedor.nombre} → ${proveedor.nombre} S.A.`);
});

test('correcciones: Oficina and Consulta see no way to correct, void or deactivate, and the server refuses it', async ({ request, browser }) => {
  const proveedor = await crearProveedorConIdPorApi(request);
  const { id, ticket } = await registrarRecepcionPorApi(request, proveedor.id, { litros: 1000 });

  for (const rol of ['Oficina', 'Consulta'] as const) {
    const persona = await crearUsuarioListoPorApi(request, rol);
    const contexto = await browser.newContext();
    const otra = await contexto.newPage();
    await loginComo(otra, persona);
    await otra.goto(`/proveedores/${proveedor.id}`);
    await expect(otra.getByRole('heading', { name: proveedor.nombre })).toBeVisible();
    await expect(otra.getByRole('cell', { name: String(ticket), exact: true })).toBeVisible();
    await expect(otra.getByRole('columnheader', { name: 'Acciones' })).toHaveCount(0);
    await expect(otra.getByRole('button', { name: /^Acciones de la recepción/ })).toHaveCount(0);
    await expect(otra.getByRole('button', { name: 'Desactivar' })).toHaveCount(0);
    await expect(otra.getByRole('button', { name: 'Corregir nombre' })).toHaveCount(0);
    await expect(otra.getByRole('button', { name: 'Historial' })).toBeVisible(); // they can read it
    await otra.goto(`/recepciones/${id}/corregir`);
    await expect(otra).not.toHaveURL(/corregir/); // the route sends them away
    await contexto.close();

    const login = await request.post('/api/Auth/login', { data: { username: persona.username, password: PASSWORD_PROPIA } });
    const headers = { Authorization: `Bearer ${(await login.json()).token}` };
    expect((await request.put(`/api/Recepciones/${id}`, { headers, data: { motivo: 'Intento no permitido' } })).status()).toBe(403);
    expect((await request.post(`/api/Recepciones/${id}/anular`, { headers, data: { motivo: 'Intento no permitido' } })).status()).toBe(403);
    expect((await request.put(`/api/Proveedores/${proveedor.id}`, { headers, data: { nombre: 'x', motivo: 'Intento no permitido', version: 'AA==' } })).status()).toBe(403);
    expect((await request.post(`/api/Proveedores/${proveedor.id}/desactivar`, { headers, data: { motivo: 'Intento no permitido' } })).status()).toBe(403);
    expect((await request.post(`/api/Proveedores/${proveedor.id}/reactivar`, { headers, data: { motivo: 'Intento no permitido' } })).status()).toBe(403);
    expect((await request.get(`/api/Recepciones/${id}/historial`, { headers })).status()).toBe(200);
    // and nothing is deleted by anybody
    expect((await request.delete(`/api/Recepciones/${id}`, { headers })).status()).toBeGreaterThanOrEqual(403);
  }
  // the recepción is untouched
  const admin = await cabecerasAutenticadas(request);
  expect((await (await request.get(`/api/Recepciones/${id}`, { headers: admin })).json()).litros).toBe(1000);
});
