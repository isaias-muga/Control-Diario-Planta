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

const DIALOGOS = '[role="dialog"], [role="alertdialog"]';

/**
 * Waits until the dialog on screen (the last one opened) is ready to be typed into: its opening animation is over and the focus is
 * inside it. On a slow CI runner a fill into the second field of a freshly opened dialog once ended up in the first one (the focus was
 * not where the test expected it; the app moves it only once, when the dialog opens, see dialogos.spec.ts). Returns at once when no
 * dialog is open.
 */
export async function esperarDialogoListo(page: Page): Promise<void> {
  await expect
    .poll(
      () =>
        page.evaluate((selector) => {
          const dialogo = Array.from(document.querySelectorAll<HTMLElement>(selector)).pop();
          if (!dialogo) {
            return true;
          }
          const animando = dialogo
            .getAnimations({ subtree: true })
            .some((animacion) => animacion.effect?.getComputedTiming().iterations !== Infinity);
          return dialogo.contains(document.activeElement) && !animando;
        }, DIALOGOS),
      { message: 'the dialog never got ready (animation over and the focus inside it)', timeout: 10_000 },
    )
    .toBe(true);
}

/**
 * Opens a dialog by clicking its opener and returns it once it is ready (see esperarDialogoListo). Every test that opens a dialog goes
 * through here, so none of them has to remember the wait.
 */
export async function abrirDialogo(abrir: Locator): Promise<Locator> {
  const page = abrir.page();
  await abrir.click();
  const dialogo = page.getByRole('dialog').or(page.getByRole('alertdialog'));
  await expect(dialogo).toBeVisible();
  await esperarDialogoListo(page);
  return dialogo;
}

/**
 * Fills a field and checks that the value stuck. With tracing on, a fill typed right after a Material dialog opened was lost now and
 * then (a different field each time). The fill waits for any open dialog to be ready first, and a retry never types on top of what an
 * earlier attempt left: the field must be focused, and it is cleared before it is filled again.
 */
export async function rellenar(campo: Locator, valor: string): Promise<void> {
  await esperarDialogoListo(campo.page());
  await expect(async () => {
    await campo.focus();
    await expect(campo).toBeFocused({ timeout: 1_000 });
    await campo.clear();
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

/** Admin and Oficina start on Recepciones (the "Hoy" filter); Consulta starts on Resumen. */
export async function loginYEsperarInicio(page: Page): Promise<void> {
  await login(page);
  await expect(page.getByRole('heading', { name: 'Recepciones', exact: true })).toBeVisible();
}

/** Creates a proveedor with a unique name through the UI and opens its detail page. */
export async function crearProveedor(page: Page): Promise<string> {
  const nombre = nombreUnico();
  await page.getByRole('link', { name: 'Proveedores' }).click();
  const dialogo = await abrirDialogo(page.getByRole('button', { name: 'Nuevo proveedor' }));
  await rellenar(dialogo.getByLabel('Nombre'), nombre);
  await dialogo.getByRole('button', { name: 'Crear' }).click();
  await page.getByRole('link', { name: new RegExp(nombre) }).click();
  await expect(page.getByRole('heading', { name: nombre })).toBeVisible();
  return nombre;
}

export interface ValoresRecepcion {
  litros: number | string;
  acidez: number | string;
  temperatura: number | string;
  ticket?: number | string;
  /** The silo button to press (1..N); 1 when not given. */
  silo?: number;
}

/**
 * Opens the 3-step registration flow from a proveedor's detail page ("Registrar recepción"): the proveedor is already chosen. Waits until
 * the page is really ready (the proveedor chosen, the silo buttons there): they only exist once the data has arrived, and acting on the
 * page before that is a race that CI, which is slower than a developer machine, loses.
 */
export async function abrirRegistro(page: Page): Promise<void> {
  await page.getByRole('link', { name: 'Registrar recepción' }).click();
  await expect(page.getByRole('heading', { name: 'Nueva recepción' })).toBeVisible();
  await expect(page.getByRole('combobox', { name: /Proveedor/ })).toContainText(/\S/);
  await expect(page.getByRole('radio', { name: '1', exact: true })).toBeVisible();
}

/** Fills step 1 (the proveedor is already chosen or the caller chose it): the readings, the silo and the ticket (a unique one when not given). */
export async function llenarRecepcion(page: Page, valores: ValoresRecepcion): Promise<void> {
  await rellenar(page.getByLabel(/^Litros/), String(valores.litros));
  await rellenar(page.getByLabel(/^Acidez/), String(valores.acidez));
  await rellenar(page.getByLabel(/^Temp/), String(valores.temperatura));
  await page.getByRole('radio', { name: String(valores.silo ?? 1), exact: true }).click();
  await rellenar(page.getByLabel(/^Ticket/), String(valores.ticket ?? ticketUnico()));
}

/** Step 1 -> step 2 ("Revisar datos"). */
export async function revisarDatos(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Revisar datos' }).click();
}

/** Step 2 -> step 3 ("Confirmar") and waits for the success screen. */
export async function confirmarRecepcion(page: Page): Promise<void> {
  await expect(page.getByRole('heading', { name: '¿Son correctos estos datos?' })).toBeVisible();
  await page.getByRole('button', { name: 'Confirmar' }).click();
  await expect(page.getByRole('heading', { name: 'Recepción guardada' })).toBeVisible();
}

/**
 * Registers a recepción through the 3-step flow, starting on a proveedor's detail page, and comes back to that page (so the callers can
 * look at the proveedor's list). The silo is 1 and the ticket a unique one unless given.
 */
export async function registrarRecepcion(page: Page, valores: ValoresRecepcion): Promise<void> {
  const paginaDelProveedor = page.url();
  await abrirRegistro(page);
  await llenarRecepcion(page, valores);
  await revisarDatos(page);
  await confirmarRecepcion(page);
  await page.goto(paginaDelProveedor);
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
      cantidadSilos: actuales.cantidadSilos,
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
      cantidadSilos: actuales.cantidadSilos,
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
export async function crearUsuarioPorApi(request: APIRequestContext, rol: 'Admin' | 'Oficina' | 'Consulta' | 'Recepcion'): Promise<PersonaDePrueba> {
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
export async function crearUsuarioListoPorApi(request: APIRequestContext, rol: 'Admin' | 'Oficina' | 'Consulta' | 'Recepcion'): Promise<PersonaDePrueba> {
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

/** Plant time now minus a few minutes, as YYYY-MM-DDTHH:mm:ss (what the API takes), computed here independently of the app. */
export function ahoraEnPlantaMenos(minutos = 5): string {
  const partes = new Intl.DateTimeFormat('sv-SE', {
    timeZone: 'America/Argentina/Buenos_Aires',
    year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23',
  }).format(new Date(Date.now() - minutos * 60_000));
  return partes.replace(' ', 'T');
}

/** Creates a proveedor through the admin API and returns its id and name. */
export async function crearProveedorConIdPorApi(request: APIRequestContext): Promise<{ id: string; nombre: string }> {
  const headers = await cabecerasAutenticadas(request);
  const nombre = nombreUnico();
  const respuesta = await request.post('/api/Proveedores', { headers, data: { nombre } });
  expect(respuesta.status()).toBe(201);
  return { id: (await respuesta.json()).id as string, nombre };
}

/** Registers a recepción through the admin API (a few minutes ago, silo 1, a unique ticket unless given) and returns its id and ticket. */
export async function registrarRecepcionPorApi(
  request: APIRequestContext,
  proveedorId: string,
  valores: { litros: number; acidez?: number; temperatura?: number; ticket?: number } ,
): Promise<{ id: string; ticket: number }> {
  const headers = await cabecerasAutenticadas(request);
  const ticket = valores.ticket ?? ticketUnico();
  const respuesta = await request.post(`/api/Proveedores/${proveedorId}/recepciones`, {
    headers,
    data: { litros: valores.litros, acidez: valores.acidez ?? 17, temperatura: valores.temperatura ?? 5, silo: 1, ticket, fechaHora: ahoraEnPlantaMenos() },
  });
  expect(respuesta.status()).toBe(200);
  return { id: (await respuesta.json()).id as string, ticket };
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

/** Sets the number of silos through the API (like an Admin saving Configuración), keeping the quality limits as they are. */
export async function cambiarCantidadSilosPorApi(request: APIRequestContext, cantidadSilos: number): Promise<void> {
  const headers = await cabecerasAutenticadas(request);
  const actuales = await (await request.get('/api/Configuracion/calidad', { headers })).json();
  const guardado = await request.put('/api/Configuracion/calidad', {
    headers,
    data: {
      acidezMaxima: actuales.acidezMaxima,
      temperaturaMaxima: actuales.temperaturaMaxima,
      aguaMaxima: actuales.aguaMaxima,
      cantidadSilos,
      version: actuales.version,
    },
  });
  expect(guardado.status()).toBe(200);
  expect((await guardado.json()).cantidadSilos).toBe(cantidadSilos);
}
