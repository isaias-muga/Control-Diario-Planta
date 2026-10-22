import { expect, test, type APIRequestContext, type Page } from '../fixtures';
import {
  PASSWORD_PROPIA,
  PASSWORD_TEMPORAL,
  cabecerasAutenticadas,
  crearOperarioPorApi,
  crearUsuarioListoPorApi,
  crearUsuarioPorApi,
  loginComo,
  abrirDialogo,
  rellenar,
  type PersonaDePrueba,
} from '../helpers';

// The bootstrap admin ("admin") is always a protected account. Every test here acts on it or on users and operarios it creates; none
// deactivates or changes "admin" itself (the API refuses it, which is what the first tests prove), so the other specs keep their login.

async function cabecerasDe(request: APIRequestContext, persona: PersonaDePrueba, password = PASSWORD_PROPIA): Promise<{ Authorization: string }> {
  const login = await request.post('/api/Auth/login', { data: { username: persona.username, password } });
  expect(login.status()).toBe(200);
  return { Authorization: `Bearer ${(await login.json()).token}` };
}

async function idDeUsuario(request: APIRequestContext, username: string): Promise<string> {
  const headers = await cabecerasAutenticadas(request);
  const usuarios = (await (await request.get('/api/Usuarios', { headers })).json()) as { id: string; username: string }[];
  return usuarios.find((u) => u.username === username)!.id;
}

async function irAUsuarios(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Ajustes' }).click();
  await page.getByRole('menuitem', { name: 'Usuarios' }).click();
  await expect(page.getByRole('heading', { name: 'Usuarios', exact: true })).toBeVisible();
  // The tables only exist once the users have arrived (the account of the owner is always there)
  await expect(page.getByRole('button', { name: 'Nuevo usuario' })).toBeVisible();
  await expect(page.getByRole('row', { name: /^admin\b/ })).toBeVisible();
}

test.describe('cuentas protegidas: another Admin cannot lock the owner out', () => {
  test('the API answers 403 when another Admin deactivates, demotes or resets a protected account', async ({ request }) => {
    const otroAdmin = await crearUsuarioListoPorApi(request, 'Admin');
    const headers = await cabecerasDe(request, otroAdmin);
    const adminId = await idDeUsuario(request, 'admin');

    const desactivar = await request.post(`/api/Usuarios/${adminId}/desactivar`, { headers, data: { motivo: 'Intento no permitido' } });
    const rol = await request.put(`/api/Usuarios/${adminId}/rol`, { headers, data: { rol: 'Consulta', motivo: 'Intento no permitido' } });
    const restablecer = await request.post(`/api/Usuarios/${adminId}/restablecer-password`, { headers, data: { password: 'Intento-no-permitido-1' } });

    for (const respuesta of [desactivar, rol, restablecer]) {
      expect(respuesta.status()).toBe(403);
      expect((await respuesta.json()).error).toContain('protegida');
    }
    // Nothing happened: the owner still logs in with the same password and is still an active Admin
    const usuarios = (await (await request.get('/api/Usuarios', { headers })).json()) as { username: string; rol: string; activo: boolean; protegido: boolean }[];
    const admin = usuarios.find((u) => u.username === 'admin')!;
    expect(admin).toMatchObject({ rol: 'Admin', activo: true, protegido: true });
    expect(otroAdmin.username).not.toBe('admin');
  });

  test('a user created through the API is never protected, even when the request asks for it', async ({ request }) => {
    const headers = await cabecerasAutenticadas(request);
    const sufijo = Date.now().toString(36);
    const respuesta = await request.post('/api/Usuarios', {
      headers,
      data: { username: `e2e.prot.${sufijo}`, nombre: 'Intenta protegerse', rol: 'Oficina', password: PASSWORD_TEMPORAL, protegido: true },
    });

    expect(respuesta.status()).toBe(200);
    expect((await respuesta.json()).protegido).toBe(false);
  });

  test('the Usuarios page shows a lock on the protected account and gives another Admin no way to change it', async ({ page, request }) => {
    const otroAdmin = await crearUsuarioListoPorApi(request, 'Admin');
    await loginComo(page, otroAdmin);
    await irAUsuarios(page);

    const fila = page.getByRole('row', { name: /^admin\b/ });
    await expect(fila.getByText('Protegida')).toBeVisible();
    await expect(fila.getByRole('button', { name: /Desactivar/ })).toHaveCount(0);
    await expect(fila.getByRole('button', { name: /Cambiar el rol/ })).toHaveCount(0);
    await expect(fila.getByRole('button', { name: /Restablecer/ })).toHaveCount(0);
    await expect(fila.getByRole('button', { name: /Ver el historial/ })).toBeVisible();
    // The other users are not locked
    const otra = page.getByRole('row', { name: new RegExp(otroAdmin.username) });
    await expect(otra.getByText('Protegida')).toHaveCount(0);
  });
});

test.describe('reactivar y historial de usuarios', () => {
  test('an Admin deactivates a user with a reason, reactivates it, and the user must choose a new password', async ({ page, request }) => {
    const persona = await crearUsuarioListoPorApi(request, 'Oficina');
    await loginComo(page, { username: 'admin', nombre: '' }, process.env.E2E_ADMIN_PASSWORD ?? process.env.ADMIN_PASSWORD ?? '');
    await irAUsuarios(page);
    const fila = page.getByRole('row', { name: new RegExp(persona.username) });
    await expect(fila).toBeVisible();

    // Deactivate: the reason is mandatory
    const dialogo = await abrirDialogo(fila.getByRole('button', { name: `Desactivar a ${persona.username}` }));
    await dialogo.getByRole('button', { name: 'Desactivar' }).click();
    await expect(dialogo).toContainText('Escribí el motivo');
    await rellenar(dialogo.getByLabel('Motivo'), 'Dejó la empresa');
    await dialogo.getByRole('button', { name: 'Desactivar' }).click();
    await expect(fila.getByText('Desactivado')).toBeVisible();
    await expect(fila.getByRole('button', { name: /^Reactivar/ })).toBeVisible();
    // It cannot log in
    expect((await request.post('/api/Auth/login', { data: { username: persona.username, password: PASSWORD_PROPIA } })).status()).toBe(401);

    // Reactivate
    const dialogoReactivar = await abrirDialogo(fila.getByRole('button', { name: `Reactivar a ${persona.username}` }));
    await rellenar(dialogoReactivar.getByLabel('Motivo'), 'Volvió a trabajar');
    await dialogoReactivar.getByRole('button', { name: 'Reactivar' }).click();
    await expect(fila.getByText('Contraseña pendiente')).toBeVisible();

    // It logs in again, but everything is refused until it chooses a password of its own
    const login = await request.post('/api/Auth/login', { data: { username: persona.username, password: PASSWORD_PROPIA } });
    expect(login.status()).toBe(200);
    expect((await login.json()).debeCambiarPassword).toBe(true);
    const token = (await login.json()).token as string;
    expect((await request.get('/api/Proveedores', { headers: { Authorization: `Bearer ${token}` } })).status()).toBe(403);

    // The history says who, when and why, newest first
    const historial = await abrirDialogo(fila.getByRole('button', { name: `Ver el historial de ${persona.username}` }));
    await expect(historial.getByRole('heading', { name: `Historial de ${persona.username}` })).toBeVisible();
    await expect(historial.getByText('Motivo: Volvió a trabajar')).toBeVisible();
    await expect(historial.getByText('Motivo: Dejó la empresa')).toBeVisible();
    await expect(historial.getByText('Motivo: Alta de usuario')).toBeVisible();
    await expect(historial.getByText(/\d{2}\/\d{2}\/\d{4} \d{2}:\d{2} · Administrador/).first()).toBeVisible();
  });

  test('an Admin changes the role of a user, with a reason, and the history keeps the old and the new role', async ({ page, request }) => {
    const persona = await crearUsuarioListoPorApi(request, 'Oficina');
    await loginComo(page, { username: 'admin', nombre: '' }, process.env.E2E_ADMIN_PASSWORD ?? process.env.ADMIN_PASSWORD ?? '');
    await irAUsuarios(page);
    const fila = page.getByRole('row', { name: new RegExp(persona.username) });

    const dialogo = await abrirDialogo(fila.getByRole('button', { name: `Cambiar el rol de ${persona.username}` }));
    await dialogo.getByRole('combobox', { name: /Rol nuevo/ }).click();
    await page.getByRole('option', { name: 'Consulta (solo lectura)' }).click();
    await rellenar(dialogo.getByLabel('Motivo'), 'Pasa a solo lectura');
    await dialogo.getByRole('button', { name: 'Cambiar rol' }).click();
    await expect(fila.getByText('Consulta (solo lectura)')).toBeVisible();

    const historial = await abrirDialogo(fila.getByRole('button', { name: `Ver el historial de ${persona.username}` }));
    await expect(historial.getByText('Motivo: Pasa a solo lectura')).toBeVisible();
    await expect(historial.getByText('Rol:').first()).toBeVisible();
    await expect(historial.getByText('Oficina').first()).toBeVisible();
  });

  test('the API refuses a deactivation without a reason', async ({ request }) => {
    const persona = await crearUsuarioPorApi(request, 'Oficina');
    const headers = await cabecerasAutenticadas(request);
    const id = await idDeUsuario(request, persona.username);

    const respuesta = await request.post(`/api/Usuarios/${id}/desactivar`, { headers, data: {} });

    expect(respuesta.status()).toBe(400);
    expect((await respuesta.json()).field).toBe('motivo');
  });
});

test.describe('operarios: reactivar y historial', () => {
  async function idDeOperario(request: APIRequestContext, nombre: string): Promise<string> {
    const headers = await cabecerasAutenticadas(request);
    const operarios = (await (await request.get('/api/Operarios', { headers })).json()) as { id: string; nombre: string }[];
    return operarios.find((o) => o.nombre === nombre)!.id;
  }

  test('an Admin deactivates and reactivates an operario from the Usuarios page, with its history', async ({ page, request }) => {
    const operario = await crearOperarioPorApi(request);
    await loginComo(page, { username: 'admin', nombre: '' }, process.env.E2E_ADMIN_PASSWORD ?? process.env.ADMIN_PASSWORD ?? '');
    await irAUsuarios(page);
    const fila = page.getByRole('row', { name: new RegExp(operario.nombre) });

    const dialogoDesactivar = await abrirDialogo(fila.getByRole('button', { name: `Desactivar a ${operario.nombre}` }));
    await rellenar(dialogoDesactivar.getByLabel('Motivo'), 'Dejó de trabajar');
    await dialogoDesactivar.getByRole('button', { name: 'Desactivar' }).click();
    await expect(fila.getByText('Desactivado')).toBeVisible();

    const dialogoReactivarOperario = await abrirDialogo(fila.getByRole('button', { name: `Reactivar a ${operario.nombre}` }));
    await rellenar(dialogoReactivarOperario.getByLabel('Motivo'), 'Volvió a trabajar');
    await dialogoReactivarOperario.getByRole('button', { name: 'Reactivar' }).click();
    await expect(fila.getByRole('button', { name: `Cambiar el PIN de ${operario.nombre}` })).toBeVisible();

    const historial = await abrirDialogo(fila.getByRole('button', { name: `Ver el historial de ${operario.nombre}` }));
    await expect(historial.getByText('Motivo: Volvió a trabajar')).toBeVisible();
    await expect(historial.getByText('Motivo: Dejó de trabajar')).toBeVisible();
    await expect(historial.getByText('Motivo: Alta de operario')).toBeVisible();
    // The history never shows a PIN
    await expect(historial).not.toContainText(operario.pin);
  });

  test('reactivating an operario whose PIN another active operario now uses answers 409', async ({ request }) => {
    const headers = await cabecerasAutenticadas(request);
    const primero = await crearOperarioPorApi(request);
    const idPrimero = await idDeOperario(request, primero.nombre);
    expect((await request.post(`/api/Operarios/${idPrimero}/desactivar`, { headers, data: { motivo: 'Dejó de trabajar' } })).status()).toBe(204);
    // Its PIN is free now: another operario takes it
    const segundo = await request.post('/api/Operarios', { headers, data: { nombre: `${primero.nombre} bis`, pin: primero.pin } });
    expect(segundo.status()).toBe(200);

    const reactivar = await request.post(`/api/Operarios/${idPrimero}/reactivar`, { headers, data: { motivo: 'Volvió a trabajar' } });

    expect(reactivar.status()).toBe(409);
    expect((await reactivar.json()).error).toContain('PIN');
    const operarios = (await (await request.get('/api/Operarios', { headers })).json()) as { id: string; activo: boolean }[];
    expect(operarios.find((o) => o.id === idPrimero)!.activo).toBe(false);
  });
});
