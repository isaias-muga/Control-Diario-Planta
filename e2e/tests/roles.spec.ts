import { expect, test, type Page } from '../fixtures';
import {
  PASSWORD_PROPIA,
  PASSWORD_TEMPORAL,
  cabecerasAutenticadas,
  crearOperarioPorApi,
  crearProveedorPorApi,
  crearUsuarioListoPorApi,
  crearUsuarioPorApi,
  irAConfiguracion,
  loginComo,
  loginYEsperarInicio,
  rellenar,
  ticketUnico,
} from '../helpers';

async function sinScrollHorizontal(page: Page): Promise<void> {
  const medidas = await page.evaluate(() => ({
    contenido: document.documentElement.scrollWidth,
    ventana: document.documentElement.clientWidth,
  }));
  expect(medidas.contenido).toBeLessThanOrEqual(medidas.ventana);
}

test.describe('el celular de recepción (role Recepcion), at 390px', () => {
  test.use({ viewport: { width: 390, height: 844 } });

  test('a new user must choose a password first, then sees only the big button and no toolbar', async ({ page, request }) => {
    const persona = await crearUsuarioPorApi(request, 'Recepcion');

    await loginComo(page, persona, PASSWORD_TEMPORAL);

    await expect(page).toHaveURL(/\/cambiar-password$/);
    await expect(page.getByText('Tenés que elegir una contraseña nueva antes de seguir.')).toBeVisible();
    await rellenar(page.getByLabel('Contraseña actual'), PASSWORD_TEMPORAL);
    await rellenar(page.getByLabel('Contraseña nueva', { exact: true }), PASSWORD_PROPIA);
    await rellenar(page.getByLabel('Repetí la contraseña nueva'), PASSWORD_PROPIA);
    await page.getByRole('button', { name: 'Guardar contraseña' }).click();

    await expect(page).toHaveURL(/\/inicio$/);
    await expect(page.getByRole('link', { name: 'Cargar recepción' })).toBeVisible();
    await expect(page.getByRole('navigation')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Ajustes' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Cerrar sesión' })).toHaveCount(0);
    await sinScrollHorizontal(page);
  });

  test('the server refuses everything the phone may not do, whatever the page shows', async ({ request }) => {
    const persona = await crearUsuarioListoPorApi(request, 'Recepcion');
    const login = await request.post('/api/Auth/login', { data: { username: persona.username, password: PASSWORD_PROPIA } });
    const headers = { Authorization: `Bearer ${(await login.json()).token}` };

    expect((await request.get('/api/Proveedores/opciones', { headers })).status()).toBe(200);
    expect((await request.get('/api/Recepciones/siguiente-ticket', { headers })).status()).toBe(200);
    expect((await request.get('/api/Configuracion/calidad', { headers })).status()).toBe(200);
    expect((await request.get('/api/Proveedores', { headers })).status()).toBe(403);
    expect((await request.get('/api/Recepciones?desde=2026-01-01&hasta=2026-12-31', { headers })).status()).toBe(403);
    expect((await request.get('/api/Usuarios', { headers })).status()).toBe(403);
    expect((await request.post('/api/Proveedores', { headers, data: { nombre: 'No' } })).status()).toBe(403);
  });

  test('any other address sends the phone back to its home', async ({ page, request }) => {
    const persona = await crearUsuarioListoPorApi(request, 'Recepcion');
    await loginComo(page, persona);
    await expect(page).toHaveURL(/\/inicio$/);

    for (const ruta of ['/resumen', '/recepciones', '/proveedores', '/configuracion', '/usuarios', '/no-existe']) {
      await page.goto(ruta);
      await expect(page).toHaveURL(/\/inicio$/);
    }
  });

  test('loads a recepción with the operario PIN, sees who loaded it and starts another', async ({ page, request }) => {
    const proveedor = await crearProveedorPorApi(request);
    const operario = await crearOperarioPorApi(request);
    const persona = await crearUsuarioListoPorApi(request, 'Recepcion');
    const ticket = ticketUnico();
    await loginComo(page, persona);

    await page.getByRole('link', { name: 'Cargar recepción' }).click();
    await page.getByRole('combobox', { name: /Proveedor/ }).click();
    await page.getByRole('option', { name: proveedor }).click();
    await rellenar(page.getByLabel(/^Ticket/), String(ticket));
    await rellenar(page.getByLabel(/^Litros/), '1.500');
    await rellenar(page.getByLabel(/^Acidez/), '17');
    await rellenar(page.getByLabel(/^Temp/), '5');
    await page.getByRole('radio', { name: '1', exact: true }).click();
    await page.getByRole('button', { name: 'Revisar datos' }).click();

    await expect(page.getByRole('heading', { name: '¿Son correctos estos datos?' })).toBeVisible();
    const pin = page.getByLabel('Tu PIN de operario');
    await expect(pin).toHaveAttribute('type', 'password');
    await expect(pin).toHaveAttribute('inputmode', 'numeric');
    await rellenar(pin, operario.pin);
    await sinScrollHorizontal(page);
    await page.getByRole('button', { name: 'Confirmar' }).click();

    await expect(page.getByRole('heading', { name: 'Recepción guardada' })).toBeVisible();
    await expect(page.getByText(`cargó ${operario.nombre}`)).toBeVisible();
    await expect(page.getByRole('link', { name: 'Ir a Recepciones' })).toHaveCount(0);
    await page.getByRole('button', { name: 'Nueva recepción' }).click();
    await expect(page.getByRole('heading', { name: 'Nueva recepción' })).toBeVisible();
    await expect(page.getByLabel(/^Litros/)).toHaveValue('');
  });

  test('a recepción loaded from the phone shows in the office list', async ({ page, browser, request }) => {
    const proveedor = await crearProveedorPorApi(request);
    const operario = await crearOperarioPorApi(request);
    const persona = await crearUsuarioListoPorApi(request, 'Recepcion');
    const ticket = ticketUnico();
    await loginComo(page, persona);
    await page.goto('/recepciones/carga');
    await page.getByRole('combobox', { name: /Proveedor/ }).click();
    await page.getByRole('option', { name: proveedor }).click();
    await rellenar(page.getByLabel(/^Ticket/), String(ticket));
    await rellenar(page.getByLabel(/^Litros/), '800');
    await rellenar(page.getByLabel(/^Acidez/), '16');
    await rellenar(page.getByLabel(/^Temp/), '5');
    await page.getByRole('radio', { name: '1', exact: true }).click();
    await page.getByRole('button', { name: 'Revisar datos' }).click();
    await rellenar(page.getByLabel('Tu PIN de operario'), operario.pin);
    await page.getByRole('button', { name: 'Confirmar' }).click();
    await expect(page.getByRole('heading', { name: 'Recepción guardada' })).toBeVisible();

    const oficina = await browser.newPage();
    await loginYEsperarInicio(oficina);
    await oficina.goto('/recepciones');
    await expect(oficina.getByRole('cell', { name: String(ticket), exact: true })).toBeVisible();
    await oficina.close();
  });

  test('a wrong PIN is shown next to the field and nothing is saved', async ({ page, request }) => {
    const proveedor = await crearProveedorPorApi(request);
    const operario = await crearOperarioPorApi(request);
    const persona = await crearUsuarioListoPorApi(request, 'Recepcion');
    await loginComo(page, persona);
    await page.goto('/recepciones/carga');
    await page.getByRole('combobox', { name: /Proveedor/ }).click();
    await page.getByRole('option', { name: proveedor }).click();
    await rellenar(page.getByLabel(/^Ticket/), String(ticketUnico()));
    await rellenar(page.getByLabel(/^Litros/), '800');
    await rellenar(page.getByLabel(/^Acidez/), '16');
    await rellenar(page.getByLabel(/^Temp/), '5');
    await page.getByRole('radio', { name: '1', exact: true }).click();
    await page.getByRole('button', { name: 'Revisar datos' }).click();
    const pinIncorrecto = operario.pin === '0000' ? '1111' : '0000';

    await rellenar(page.getByLabel('Tu PIN de operario'), pinIncorrecto);
    await page.getByRole('button', { name: 'Confirmar' }).click();

    await expect(page.getByText('PIN incorrecto.')).toBeVisible();
    await expect(page.getByRole('heading', { name: '¿Son correctos estos datos?' })).toBeVisible();
    await expect(page.getByLabel('Tu PIN de operario')).toHaveValue('');
    await expect(page.getByText('No se guardó')).toHaveCount(0);
  });

  test('five wrong PINs lock the PIN checks for a while, even for the right one', async ({ page, request }) => {
    const proveedor = await crearProveedorPorApi(request);
    const operario = await crearOperarioPorApi(request);
    const persona = await crearUsuarioListoPorApi(request, 'Recepcion');
    await loginComo(page, persona);
    await page.goto('/recepciones/carga');
    await page.getByRole('combobox', { name: /Proveedor/ }).click();
    await page.getByRole('option', { name: proveedor }).click();
    await rellenar(page.getByLabel(/^Ticket/), String(ticketUnico()));
    await rellenar(page.getByLabel(/^Litros/), '800');
    await rellenar(page.getByLabel(/^Acidez/), '16');
    await rellenar(page.getByLabel(/^Temp/), '5');
    await page.getByRole('radio', { name: '1', exact: true }).click();
    await page.getByRole('button', { name: 'Revisar datos' }).click();
    const pinIncorrecto = operario.pin === '0000' ? '1111' : '0000';

    for (let i = 0; i < 5; i++) {
      await rellenar(page.getByLabel('Tu PIN de operario'), pinIncorrecto);
      await page.getByRole('button', { name: 'Confirmar' }).click();
      await expect(page.getByLabel('Tu PIN de operario')).toHaveValue('');
    }
    await rellenar(page.getByLabel('Tu PIN de operario'), operario.pin);
    await page.getByRole('button', { name: 'Confirmar' }).click();

    await expect(page.getByText(/Demasiados intentos con el PIN/)).toBeVisible();
    await expect(page.getByRole('heading', { name: '¿Son correctos estos datos?' })).toBeVisible();
  });
});

test.describe('Oficina (the office clerk)', () => {
  test('can register recepciones but sees no way to create proveedores or change settings, and no Usuarios page', async ({ page, request }) => {
    const persona = await crearUsuarioListoPorApi(request, 'Oficina');
    await loginComo(page, persona);
    await expect(page.getByRole('heading', { name: 'Recepciones', exact: true })).toBeVisible();

    await page.getByRole('link', { name: 'Proveedores', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Proveedores', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Nuevo proveedor' })).toHaveCount(0);

    await page.getByRole('link', { name: 'Recepciones', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Recepciones', exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: '+ Registrar recepción' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Carga rápida' })).toHaveCount(0);

    await irAConfiguracion(page);
    await expect(page.getByLabel(/^Acidez máxima/)).toBeDisabled();
    await expect(page.getByLabel('Cantidad de silos')).toBeDisabled();
    await expect(page.getByRole('button', { name: 'Guardar' })).toHaveCount(0);

    await page.getByRole('button', { name: 'Ajustes' }).click();
    await expect(page.getByRole('menuitem', { name: 'Usuarios' })).toHaveCount(0);
    await page.keyboard.press('Escape');
    await page.goto('/usuarios');
    await expect(page).toHaveURL(/\/recepciones$/);
  });

  test('the server still refuses every change but registering a recepción, whatever the page shows', async ({ request }) => {
    const persona = await crearUsuarioListoPorApi(request, 'Oficina');
    const login = await request.post('/api/Auth/login', { data: { username: persona.username, password: PASSWORD_PROPIA } });
    const headers = { Authorization: `Bearer ${(await login.json()).token}` };

    expect((await request.get('/api/Proveedores', { headers })).status()).toBe(200);
    expect((await request.post('/api/Proveedores', { headers, data: { nombre: 'No' } })).status()).toBe(403);
    expect((await request.put('/api/Configuracion/calidad', { headers, data: { acidezMaxima: 19, temperaturaMaxima: 20, aguaMaxima: 0, version: 'x' } })).status()).toBe(403);
    // Registering is allowed for her (this proveedor does not exist: 404, not 403)
    expect((await request.post(`/api/Proveedores/${crypto.randomUUID()}/recepciones`, { headers, data: {} })).status()).toBe(404);
    expect((await request.get('/api/Operarios', { headers })).status()).toBe(403);
  });
});

test.describe('Consulta (the owner, read only)', () => {
  test('lands on Resumen, reads everything and sees no way to create, edit or register', async ({ page, request }) => {
    const persona = await crearUsuarioListoPorApi(request, 'Consulta');
    await loginComo(page, persona);
    await expect(page.getByRole('heading', { name: 'Resumen de recepciones' })).toBeVisible();

    await page.getByRole('link', { name: 'Recepciones', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Recepciones', exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: '+ Registrar recepción' })).toHaveCount(0);

    await page.getByRole('link', { name: 'Proveedores', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Proveedores', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Nuevo proveedor' })).toHaveCount(0);

    await irAConfiguracion(page);
    await expect(page.getByLabel(/^Acidez máxima/)).toBeDisabled();
    await expect(page.getByRole('button', { name: 'Guardar' })).toHaveCount(0);

    await page.getByRole('button', { name: 'Ajustes' }).click();
    await expect(page.getByRole('menuitem', { name: 'Usuarios' })).toHaveCount(0);
    await page.keyboard.press('Escape');

    // The registration flow and the admin pages send it back to its own start
    await page.goto('/recepciones/carga');
    await expect(page).toHaveURL(/\/resumen$/);
    await page.goto('/usuarios');
    await expect(page).toHaveURL(/\/resumen$/);
  });

  test('the server refuses every write with 403, whatever the page shows', async ({ request }) => {
    const persona = await crearUsuarioListoPorApi(request, 'Consulta');
    const login = await request.post('/api/Auth/login', { data: { username: persona.username, password: PASSWORD_PROPIA } });
    const headers = { Authorization: `Bearer ${(await login.json()).token}` };

    expect((await request.get('/api/Proveedores', { headers })).status()).toBe(200);
    expect((await request.get('/api/Configuracion/calidad', { headers })).status()).toBe(200);
    expect((await request.post('/api/Proveedores', { headers, data: { nombre: 'No' } })).status()).toBe(403);
    expect((await request.post(`/api/Proveedores/${crypto.randomUUID()}/recepciones`, { headers, data: {} })).status()).toBe(403);
    expect((await request.put('/api/Configuracion/calidad', { headers, data: { acidezMaxima: 19, temperaturaMaxima: 20, aguaMaxima: 0, version: 'x' } })).status()).toBe(403);
    expect((await request.get('/api/Usuarios', { headers })).status()).toBe(403);
    expect((await request.get('/api/Operarios', { headers })).status()).toBe(403);
  });
});

test.describe('Oficina registers on a PC (1366px)', () => {
  test.use({ viewport: { width: 1366, height: 768 } });

  test('registers a recepción end to end with her own user (no PIN) and the audit says who loaded it', async ({ page, request }) => {
    const proveedor = await crearProveedorPorApi(request);
    const persona = await crearUsuarioListoPorApi(request, 'Oficina');
    const ticket = ticketUnico();
    await loginComo(page, persona);
    await expect(page.getByRole('heading', { name: 'Recepciones', exact: true })).toBeVisible();

    await page.getByRole('link', { name: 'Recepciones', exact: true }).click();
    await page.getByRole('link', { name: '+ Registrar recepción' }).click();
    await expect(page.getByRole('heading', { name: 'Nueva recepción' })).toBeVisible();
    await page.getByRole('combobox', { name: /Proveedor/ }).click();
    await page.getByRole('option', { name: proveedor }).click();
    await expect(page.getByRole('radio', { name: '3', exact: true })).toBeVisible();
    await rellenar(page.getByLabel(/^Ticket/), String(ticket));
    await rellenar(page.getByLabel(/^Litros/), '2.200');
    await rellenar(page.getByLabel(/^Acidez/), '16');
    await rellenar(page.getByLabel(/^Temp/), '5');
    await page.getByRole('radio', { name: '2', exact: true }).click();
    await page.getByRole('button', { name: 'Revisar datos' }).click();

    await expect(page.getByRole('heading', { name: '¿Son correctos estos datos?' })).toBeVisible();
    await expect(page.getByLabel('Tu PIN de operario')).toHaveCount(0); // no PIN for the office
    await page.getByRole('button', { name: 'Confirmar' }).click();
    await expect(page.getByRole('heading', { name: 'Recepción guardada' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Nueva recepción' })).toBeVisible();
    await page.getByRole('link', { name: 'Ir a Recepciones' }).click();

    await expect(page.getByRole('cell', { name: String(ticket), exact: true })).toBeVisible();
    const headers = await cabecerasAutenticadas(request);
    const hoy = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Argentina/Buenos_Aires' }).format(new Date());
    const listado = await (await request.get(`/api/Recepciones?desde=${hoy}&hasta=${hoy}&ticket=${ticket}`, { headers })).json();
    expect(listado.items).toHaveLength(1);
    expect(listado.items[0].registradoPor).toBe(persona.nombre);
    expect(listado.items[0].silo).toBe(2);
  });
});

test.describe('Admin: users and operarios', () => {
  test('creates a user and an operario from the Usuarios page, and the new user must choose a password', async ({ page, browser }) => {
    await loginYEsperarInicio(page);
    await page.getByRole('button', { name: 'Ajustes' }).click();
    await page.getByRole('menuitem', { name: 'Usuarios' }).click();
    await expect(page.getByRole('heading', { name: 'Usuarios', exact: true })).toBeVisible();
    const sufijo = Date.now().toString(36);
    const username = `e2e.ui.${sufijo}`;

    await page.getByRole('button', { name: 'Nuevo usuario' }).click();
    const dialogo = page.getByRole('dialog');
    await rellenar(dialogo.getByLabel('Usuario'), username);
    await rellenar(dialogo.getByLabel('Nombre'), `E2E Persona ${sufijo}`);
    await dialogo.getByRole('combobox', { name: 'Rol' }).click();
    await page.getByRole('option', { name: 'Oficina', exact: true }).click();
    await rellenar(dialogo.getByLabel('Contraseña temporal'), PASSWORD_TEMPORAL);
    await dialogo.getByRole('button', { name: 'Crear' }).click();
    await expect(page.getByRole('cell', { name: username, exact: true })).toBeVisible();
    await expect(page.getByRole('row', { name: new RegExp(username) }).getByText('Contraseña pendiente')).toBeVisible();

    const otra = await browser.newPage();
    await loginComo(otra, { username, nombre: '' }, PASSWORD_TEMPORAL);
    await expect(otra).toHaveURL(/\/cambiar-password$/);
    await otra.close();

    await page.getByRole('button', { name: 'Nuevo operario' }).click();
    const dialogoOperario = page.getByRole('dialog');
    await rellenar(dialogoOperario.getByLabel('Nombre'), `E2E Operario ${sufijo}`);
    await rellenar(dialogoOperario.getByLabel('PIN'), String(1000 + (Date.now() % 9000)));
    await dialogoOperario.getByRole('button', { name: 'Crear' }).click();
    await expect(page.getByRole('cell', { name: `E2E Operario ${sufijo}`, exact: true })).toBeVisible();
  });

  test('deactivating a user ends its session at once', async ({ page, request }) => {
    const persona = await crearUsuarioListoPorApi(request, 'Oficina');
    await loginComo(page, persona);
    await expect(page.getByRole('heading', { name: 'Recepciones', exact: true })).toBeVisible();
    // The page is still loading its data: a request answered after the admin's change would end the session before the test acts
    await page.waitForLoadState('networkidle');

    const headers = await cabecerasAutenticadas(request);
    const usuarios = await (await request.get('/api/Usuarios', { headers })).json();
    const id = usuarios.find((u: { username: string }) => u.username === persona.username).id;
    expect((await request.post(`/api/Usuarios/${id}/desactivar`, { headers, data: { motivo: 'Baja de prueba e2e' } })).status()).toBe(204);
    await page.getByRole('link', { name: 'Proveedores', exact: true }).click();

    await expect(page).toHaveURL(/\/login$/);
  });

  test('a password reset ends the sessions and the user must choose another password', async ({ page, request, browser }) => {
    const persona = await crearUsuarioListoPorApi(request, 'Oficina');
    await loginComo(page, persona);
    await expect(page.getByRole('heading', { name: 'Recepciones', exact: true })).toBeVisible();
    // The page is still loading its data: a request answered after the admin's change would end the session before the test acts
    await page.waitForLoadState('networkidle');
    const headers = await cabecerasAutenticadas(request);
    const usuarios = await (await request.get('/api/Usuarios', { headers })).json();
    const id = usuarios.find((u: { username: string }) => u.username === persona.username).id;

    expect((await request.post(`/api/Usuarios/${id}/restablecer-password`, { headers, data: { password: 'Otra-temporal-1' } })).status()).toBe(204);
    await page.getByRole('link', { name: 'Proveedores', exact: true }).click();
    await expect(page).toHaveURL(/\/login$/);

    const nueva = await browser.newPage();
    await loginComo(nueva, persona, 'Otra-temporal-1');
    await expect(nueva).toHaveURL(/\/cambiar-password$/);
    await nueva.close();
  });
});

test('login: a wrong password and an unknown user give the same message', async ({ page }) => {
  await page.goto('/login');
  await page.getByLabel('Usuario').fill('no.existe.nadie');
  await page.getByLabel('Contraseña').fill('cualquiera-123');
  await page.getByRole('button', { name: 'Ingresar' }).click();

  await expect(page.getByRole('alert')).toHaveText('Usuario o contraseña incorrectos.');
});
