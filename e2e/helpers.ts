import { expect, type APIRequestContext, type Locator, type Page } from '@playwright/test';

let counter = 0;

// Unique per test run and per call: runs against a database that is not fresh must not collide.
function uniqueSuffix(): string {
  counter += 1;
  return `${Date.now().toString(36)}${counter}`;
}

/** A ticket number that no other test (or previous run) has used. Ticket is a positive 32-bit int. */
export function ticketUnico(): number {
  counter += 1;
  return 1_000_000 + ((Date.now() % 1_000_000_000) + counter) % 1_000_000_000;
}

export function nombreUnico(): string {
  return `E2E ${uniqueSuffix()}`;
}

function adminPassword(): string {
  const password = process.env.E2E_ADMIN_PASSWORD ?? process.env.ADMIN_PASSWORD;
  if (!password) {
    throw new Error('Set E2E_ADMIN_PASSWORD (or ADMIN_PASSWORD in ../.env) to log in.');
  }
  return password;
}

/**
 * Fills a field and checks that the value stuck. With tracing on, a fill typed right after a Material dialog
 * opens was lost now and then (a different field each time), so retry until the value is really there.
 */
export async function rellenar(campo: Locator, valor: string): Promise<void> {
  await expect(async () => {
    await campo.fill(valor);
    await expect(campo).toHaveValue(valor, { timeout: 1_000 });
  }).toPass({ timeout: 10_000 });
}

export async function login(page: Page, password = adminPassword(), usuario = 'admin'): Promise<void> {
  await page.goto('/login');
  await page.getByLabel('Usuario').fill(usuario);
  await page.getByLabel('Contraseña').fill(password);
  await page.getByRole('button', { name: 'Ingresar' }).click();
}

export async function loginYEsperarResumen(page: Page): Promise<void> {
  await login(page);
  await expect(page.getByRole('heading', { name: 'Resumen de recepciones' })).toBeVisible();
}

/** Creates a proveedor with a unique name through the UI and opens its detail page. */
export async function crearProveedor(page: Page): Promise<string> {
  const nombre = nombreUnico();
  await page.getByRole('link', { name: 'Proveedores' }).click();
  await page.getByRole('button', { name: 'Nuevo proveedor' }).click();
  await rellenar(page.getByRole('dialog').getByLabel('Nombre'), nombre);
  await page.getByRole('dialog').getByRole('button', { name: 'Crear' }).click();
  await page.getByRole('link', { name: new RegExp(nombre) }).click();
  await expect(page.getByRole('heading', { name: nombre })).toBeVisible();
  return nombre;
}

export interface ValoresRecepcion {
  litros: number | string;
  acidez: number | string;
  temperatura: number | string;
  ticket?: number | string;
}

/**
 * Opens the registration dialog of a proveedor's detail page and waits until the dialog has put the focus on
 * Litros (its initial focus). Material moves the focus after the opening animation: typing into a field before
 * that can land in the field that receives the focus.
 */
export async function abrirRegistro(page: Page): Promise<Locator> {
  await page.getByRole('button', { name: 'Registrar recepción' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByLabel(/^Litros/)).toBeFocused();
  return dialog;
}

/** Cancelar on a form that has data asks first: confirm the discard and wait until the dialog is gone. */
export async function descartarRegistro(page: Page): Promise<void> {
  await page.getByRole('dialog').getByRole('button', { name: 'Cancelar' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Descartar' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
}

/** Opens the registration dialog on a proveedor's detail page, fills it and presses "Registrar". */
export async function registrarRecepcion(page: Page, valores: ValoresRecepcion): Promise<void> {
  const dialog = await abrirRegistro(page);
  await rellenar(dialog.getByLabel(/^Litros/), String(valores.litros));
  await rellenar(dialog.getByLabel(/^Acidez/), String(valores.acidez));
  await rellenar(dialog.getByLabel(/^Temperatura/), String(valores.temperatura));
  if (valores.ticket !== undefined) {
    await rellenar(dialog.getByLabel(/^Ticket/), String(valores.ticket));
  }
  await dialog.getByRole('button', { name: 'Registrar', exact: true }).click();
}

/** Default acidez máxima (LimitesCalidad.Predeterminados). Tests that change it restore this value. */
export const ACIDEZ_MAXIMA_PREDETERMINADA = 19;

/**
 * Presses "Guardar" on Configuración and waits until the save really finished: the PUT answered 200 (the real
 * signal; the snackbar is only a UI confirmation and may be left over from an earlier save). The "Última modificación" text is visible from the first load, so it proves nothing.
 */
export async function guardarLimites(page: Page): Promise<void> {
  const respuesta = page.waitForResponse(
    (r) => r.url().endsWith('/api/Configuracion/calidad') && r.request().method() === 'PUT',
  );
  await page.getByRole('button', { name: 'Guardar' }).click();
  expect((await respuesta).status()).toBe(200);
  await expect(page.getByText('Configuración guardada.').first()).toBeVisible();
}

/** Opens Configuración from the settings menu of the toolbar (the gear). */
export async function irAConfiguracion(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Ajustes' }).click();
  await page.getByRole('menuitem', { name: 'Configuración' }).click();
}

/** Logs in through the API (no page needed) and returns the headers of an authenticated request. */
let tokenDeAdmin: string | null = null;

export async function cabecerasAutenticadas(request: APIRequestContext): Promise<{ Authorization: string }> {
  // One login for the whole run (the token lasts hours): the login endpoint is rate limited per address
  if (tokenDeAdmin) {
    return { Authorization: `Bearer ${tokenDeAdmin}` };
  }
  const login = await request.post('/api/Auth/login', { data: { username: 'admin', password: adminPassword() } });
  expect(login.status()).toBe(200);
  tokenDeAdmin = (await login.json()).token as string;
  return { Authorization: `Bearer ${tokenDeAdmin}` };
}

/**
 * Sets acidez máxima through the API, like another user saving the Configuración page. Sends the version it has just
 * read, so the save is accepted and the stored version changes (a page that loaded before is now stale).
 */
export async function cambiarAcidezMaximaPorApi(request: APIRequestContext, acidezMaxima: number): Promise<void> {
  const headers = await cabecerasAutenticadas(request);
  const actuales = await (await request.get('/api/Configuracion/calidad', { headers })).json();
  const guardado = await request.put('/api/Configuracion/calidad', {
    headers,
    data: {
      acidezMaxima,
      temperaturaMaxima: actuales.temperaturaMaxima,
      aguaMaxima: actuales.aguaMaxima,
      version: actuales.version,
    },
  });
  expect(guardado.status()).toBe(200);
}

/**
 * Puts acidez máxima back to its default through the API (own login, no page needed), so it also works when the
 * test timed out or the page is gone, and checks the stored value. The other two limits are left as they are.
 */
export async function restaurarAcidezMaxima(request: APIRequestContext): Promise<void> {
  const headers = await cabecerasAutenticadas(request);

  const actuales = await (await request.get('/api/Configuracion/calidad', { headers })).json();
  const guardado = await request.put('/api/Configuracion/calidad', {
    headers,
    data: {
      acidezMaxima: ACIDEZ_MAXIMA_PREDETERMINADA,
      temperaturaMaxima: actuales.temperaturaMaxima,
      aguaMaxima: actuales.aguaMaxima,
      version: actuales.version,
    },
  });
  expect(guardado.status()).toBe(200);
  expect((await guardado.json()).acidezMaxima).toBe(ACIDEZ_MAXIMA_PREDETERMINADA);
}

/** The temporary password every user created by these helpers starts with (it must be changed at the first login). */
export const PASSWORD_TEMPORAL = 'Temporal-12345';
export const PASSWORD_PROPIA = 'Propia-12345-xyz';

export interface PersonaDePrueba {
  username: string;
  nombre: string;
}

/** Creates a user of that role through the admin API, with the temporary password. */
export async function crearUsuarioPorApi(request: APIRequestContext, rol: 'Admin' | 'Oficina' | 'Recepcion'): Promise<PersonaDePrueba> {
  const headers = await cabecerasAutenticadas(request);
  const sufijo = uniqueSuffix().replace(/[^a-z0-9]/gi, '');
  const persona = { username: `e2e.${rol.toLowerCase()}.${sufijo}`, nombre: `E2E ${rol} ${sufijo}` };
  const respuesta = await request.post('/api/Usuarios', { headers, data: { ...persona, rol, password: PASSWORD_TEMPORAL } });
  expect(respuesta.status()).toBe(200);
  return persona;
}

/** The user chooses a password of their own through the API (what the forced change page does). Returns the new token. */
export async function elegirPasswordPorApi(request: APIRequestContext, username: string): Promise<string> {
  const login = await request.post('/api/Auth/login', { data: { username, password: PASSWORD_TEMPORAL } });
  expect(login.status()).toBe(200);
  const { token } = await login.json();
  const cambio = await request.post('/api/Auth/cambiar-password', {
    headers: { Authorization: `Bearer ${token}` },
    data: { passwordActual: PASSWORD_TEMPORAL, passwordNueva: PASSWORD_PROPIA },
  });
  expect(cambio.status()).toBe(200);
  return (await cambio.json()).token;
}

/** A user that already chose its password, ready to log in with PASSWORD_PROPIA. */
export async function crearUsuarioListoPorApi(request: APIRequestContext, rol: 'Admin' | 'Oficina' | 'Recepcion'): Promise<PersonaDePrueba> {
  const persona = await crearUsuarioPorApi(request, rol);
  await elegirPasswordPorApi(request, persona.username);
  return persona;
}

/** Creates an operario with a PIN nobody else uses (only active operarios must differ, so it retries on a clash). */
export async function crearOperarioPorApi(request: APIRequestContext): Promise<{ nombre: string; pin: string }> {
  const headers = await cabecerasAutenticadas(request);
  const nombre = `E2E Operario ${uniqueSuffix()}`;
  for (let intento = 0; intento < 40; intento++) {
    const pin = String(Math.floor(Math.random() * 10_000)).padStart(4, '0');
    const respuesta = await request.post('/api/Operarios', { headers, data: { nombre, pin } });
    if (respuesta.status() === 200) {
      return { nombre, pin };
    }
    expect(respuesta.status()).toBe(409);
  }
  throw new Error('No free PIN found for the test operario.');
}

/** Creates a proveedor through the admin API and returns its name. */
export async function crearProveedorPorApi(request: APIRequestContext): Promise<string> {
  const headers = await cabecerasAutenticadas(request);
  const nombre = nombreUnico();
  const respuesta = await request.post('/api/Proveedores', { headers, data: { nombre } });
  expect(respuesta.status()).toBe(201);
  return nombre;
}

/** Logs in through the login page as that user (with the password of the helpers) and waits for the first screen. */
export async function loginComo(page: Page, persona: PersonaDePrueba, password = PASSWORD_PROPIA): Promise<void> {
  await login(page, password, persona.username);
  // Wait for the session to be stored: leaving the login page is the sign that the token arrived
  await expect(page).not.toHaveURL(/\/login/);
}
