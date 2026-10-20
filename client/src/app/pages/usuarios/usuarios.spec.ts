import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { Usuarios } from './usuarios';
import { HistorialDialog, HistorialDialogData } from '../../components/historial-dialog/historial-dialog';
import { MotivoDialog, MotivoDialogData } from '../../components/motivo-dialog/motivo-dialog';
import { OperarioDialog } from '../../components/operario-dialog/operario-dialog';
import { UsuarioDialog } from '../../components/usuario-dialog/usuario-dialog';
import { ValorSecretoDialog, ValorSecretoDialogData } from '../../components/valor-secreto-dialog/valor-secreto-dialog';
import { Operario, Usuario } from '../../models/usuarios';
import { cerrarSesionDePrueba, iniciarSesionDePrueba } from '../../testing/sesion';

const yo: Usuario = { id: 'usuario-de-prueba', username: 'admin', nombre: 'Administrador', rol: 'Admin', activo: true, debeCambiarPassword: false, protegido: false };
const maria: Usuario = { id: 'u2', username: 'maria', nombre: 'María Pérez', rol: 'Oficina', activo: true, debeCambiarPassword: true, protegido: false };
const baja: Usuario = { id: 'u3', username: 'baja', nombre: 'Ex Empleado', rol: 'Recepcion', activo: false, debeCambiarPassword: false, protegido: false };
const dueno: Usuario = { id: 'u4', username: 'dueno', nombre: 'El Dueño', rol: 'Admin', activo: true, debeCambiarPassword: false, protegido: true };
const juan: Operario = { id: 'o1', nombre: 'Juan Pérez', activo: true };
const pedro: Operario = { id: 'o2', nombre: 'Pedro Gómez', activo: false };

describe('Usuarios', () => {
  let fixture: ComponentFixture<Usuarios>;
  let component: Usuarios;
  let http: HttpTestingController;
  let dialog: { open: ReturnType<typeof vi.fn> };

  const esUsuarios = (r: { url: string; method: string }) => r.url.endsWith('/Usuarios') && r.method === 'GET';
  const esOperarios = (r: { url: string; method: string }) => r.url.endsWith('/Operarios') && r.method === 'GET';

  beforeEach(async () => {
    iniciarSesionDePrueba('Admin');
    dialog = { open: vi.fn() };
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: MatDialog, useValue: dialog }],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Usuarios);
    component = fixture.componentInstance;
    await fixture.whenStable();
    await cargar();
  });

  afterEach(() => {
    http.verify();
    cerrarSesionDePrueba();
  });

  async function cargar(usuarios: Usuario[] = [yo, maria, baja], operarios: Operario[] = [juan]): Promise<void> {
    http.expectOne(esUsuarios).flush(usuarios);
    http.expectOne(esOperarios).flush(operarios);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function recargar(usuarios: Usuario[], operarios: Operario[] = [juan]): Promise<void> {
    component.cargar();
    await cargar(usuarios, operarios);
  }

  const el = (): HTMLElement => fixture.nativeElement;
  const filas = (tabla: number): string[] =>
    Array.from(el().querySelectorAll('table')[tabla].querySelectorAll('tr.mat-mdc-row')).map((f) => (f.textContent ?? '').replace(/\s+/g, ' ').trim());
  const botones = (fila: string): string[] => {
    const tr = Array.from(el().querySelectorAll('tr.mat-mdc-row')).find((f) => f.textContent?.includes(fila));
    return Array.from(tr?.querySelectorAll('button') ?? []).map((b) => b.textContent?.trim() ?? '');
  };
  const apretar = (fila: string, boton: string): void => {
    const tr = Array.from(el().querySelectorAll('tr.mat-mdc-row')).find((f) => f.textContent?.includes(fila));
    (Array.from(tr!.querySelectorAll('button')).find((b) => b.textContent?.trim() === boton) as HTMLButtonElement).click();
  };

  it('lists the users with their role and state, and the operarios', () => {
    expect(filas(0)).toHaveLength(3);
    expect(filas(0)[1]).toContain('maria');
    expect(filas(0)[1]).toContain('Oficina');
    expect(filas(0)[1]).toContain('Contraseña pendiente');
    expect(filas(0)[2]).toContain('Desactivado');
    expect(filas(1)).toEqual([expect.stringContaining('Juan Pérez')]);
  });

  it('offers Reactivar and Historial on a deactivated operario', async () => {
    await recargar([yo], [juan, pedro]);

    expect(botones('Juan')).toEqual(['Cambiar PIN', 'Desactivar', 'Historial']);
    expect(botones('Pedro')).toEqual(['Reactivar', 'Historial']);
  });

  it('never shows a password hash, a stamp or a PIN', () => {
    const texto = el().textContent ?? '';

    expect(texto).not.toMatch(/hash|stamp|PinHash/i);
  });

  it('offers the history on every user and the right actions on each', () => {
    expect(botones('baja')).toEqual(['Reactivar', 'Historial']);
    expect(botones('Administrador')).toEqual(['Restablecer contraseña', 'Historial']); // no way to deactivate or demote yourself
    expect(botones('maria')).toEqual(['Restablecer contraseña', 'Cambiar rol', 'Desactivar', 'Historial']);
  });

  it('shows a lock badge on a protected account and offers no deactivate, role or reset to another admin', async () => {
    await recargar([yo, maria, dueno]);

    expect(filas(0)[2]).toContain('Protegida');
    expect(botones('dueno')).toEqual(['Historial']);
    expect(el().querySelectorAll('.insignia-protegida')).toHaveLength(1);
  });

  it('lets the protected user reset their own password but not deactivate or demote themselves', async () => {
    // A new session of the protected user: AuthService reads the token when it is first injected, so the component is built again
    TestBed.resetTestingModule();
    cerrarSesionDePrueba();
    iniciarSesionDePrueba('Admin', { sub: dueno.id });
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: MatDialog, useValue: dialog }],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Usuarios);
    component = fixture.componentInstance;
    await fixture.whenStable();
    await cargar([maria, dueno]);

    expect(botones('dueno')).toEqual(['Restablecer contraseña', 'Historial']);
    expect(botones('maria')).toEqual(['Restablecer contraseña', 'Cambiar rol', 'Desactivar', 'Historial']);
  });

  it('shows an error with a retry when the list cannot be loaded', async () => {
    fixture = TestBed.createComponent(Usuarios);
    component = fixture.componentInstance;
    await fixture.whenStable();

    http.expectOne(esUsuarios).flush({ error: 'No tenés permiso para esta acción.' }, { status: 403, statusText: 'Forbidden' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.error()).toBe('No tenés permiso para esta acción.');
  });

  it('reloads the lists after a user is created', async () => {
    dialog.open.mockReturnValue({ afterClosed: () => of({ ...maria, id: 'nuevo' }) });

    component.nuevoUsuario();
    await cargar();

    expect(dialog.open.mock.calls[0][0]).toBe(UsuarioDialog);
  });

  it('does not reload when the new-user dialog is cancelled', () => {
    dialog.open.mockReturnValue({ afterClosed: () => of(undefined) });

    component.nuevoUsuario();

    http.expectNone(esUsuarios);
  });

  it('reloads after an operario is created', async () => {
    dialog.open.mockReturnValue({ afterClosed: () => of(juan) });

    component.nuevoOperario();
    await cargar();

    expect(dialog.open.mock.calls[0][0]).toBe(OperarioDialog);
  });

  it('asks for a temporary password and sends it to the API', () => {
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });

    apretar('maria', 'Restablecer contraseña');

    const [tipo, config] = dialog.open.mock.calls[0];
    expect(tipo).toBe(ValorSecretoDialog);
    const data = config.data as ValorSecretoDialogData;
    expect(data.tipo).toBe('password');
    data.guardar('temporal-123').subscribe();
    const req = http.expectOne((r) => r.url.endsWith('/Usuarios/u2/restablecer-password'));
    expect(req.request.body).toEqual({ password: 'temporal-123' });
    req.flush(null);
  });

  it('asks for a new PIN and sends it to the API', () => {
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });

    apretar('Juan', 'Cambiar PIN');

    const data = dialog.open.mock.calls[0][1].data as ValorSecretoDialogData;
    expect(data.tipo).toBe('pin');
    data.guardar('4321').subscribe();
    const req = http.expectOne((r) => r.url.endsWith('/Operarios/o1/pin'));
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ pin: '4321' });
    req.flush(null);
  });

  const datosDelMotivo = (): MotivoDialogData => dialog.open.mock.calls[0][1].data as MotivoDialogData;

  it('asks for a reason before deactivating a user and does nothing when the dialog is cancelled', () => {
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });

    apretar('maria', 'Desactivar');

    expect(dialog.open.mock.calls[0][0]).toBe(MotivoDialog);
    expect(datosDelMotivo().peligro).toBe(true);
    http.expectNone((r) => r.method === 'POST');
  });

  it('deactivates a user with the reason and reloads', async () => {
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });

    apretar('maria', 'Desactivar');
    datosDelMotivo().guardar('Dejó la empresa', null).subscribe();
    const req = http.expectOne((r) => r.url.endsWith('/Usuarios/u2/desactivar') && r.method === 'POST');
    expect(req.request.body).toEqual({ motivo: 'Dejó la empresa' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    await cargar();
  });

  it('reactivates a deactivated user with the reason and reloads', async () => {
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });

    apretar('baja', 'Reactivar');
    expect(datosDelMotivo().peligro).toBeUndefined();
    datosDelMotivo().guardar('Volvió a trabajar', null).subscribe();
    const req = http.expectOne((r) => r.url.endsWith('/Usuarios/u3/reactivar') && r.method === 'POST');
    expect(req.request.body).toEqual({ motivo: 'Volvió a trabajar' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    await cargar();
  });

  it('changes the role to the one chosen, with the reason, offering every role but the current one', async () => {
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });

    apretar('maria', 'Cambiar rol');
    const data = datosDelMotivo();
    expect(data.opcion?.opciones.map((o) => o.valor)).toEqual(['Admin', 'Consulta', 'Recepcion']);
    data.guardar('Pasa a solo lectura', null, 'Consulta').subscribe();
    const req = http.expectOne((r) => r.url.endsWith('/Usuarios/u2/rol') && r.method === 'PUT');
    expect(req.request.body).toEqual({ rol: 'Consulta', motivo: 'Pasa a solo lectura' });
    req.flush({ ...maria, rol: 'Consulta' });
    await cargar();
  });

  it('deactivates an operario with the reason and reloads', async () => {
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });

    apretar('Juan', 'Desactivar');
    datosDelMotivo().guardar('Dejó de trabajar', null).subscribe();
    const req = http.expectOne((r) => r.url.endsWith('/Operarios/o1/desactivar') && r.method === 'POST');
    expect(req.request.body).toEqual({ motivo: 'Dejó de trabajar' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    await cargar();
  });

  it('reactivates an operario with the reason and reloads', async () => {
    await recargar([yo], [pedro]);
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });

    apretar('Pedro', 'Reactivar');
    datosDelMotivo().guardar('Volvió a trabajar', null).subscribe();
    const req = http.expectOne((r) => r.url.endsWith('/Operarios/o2/reactivar') && r.method === 'POST');
    expect(req.request.body).toEqual({ motivo: 'Volvió a trabajar' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    await cargar([yo], [pedro]);
  });

  it('opens the history of a user and of an operario', () => {
    dialog.open.mockReturnValue({ afterClosed: () => of(undefined) });

    apretar('maria', 'Historial');
    const deUsuario = dialog.open.mock.calls[0][1].data as HistorialDialogData;
    apretar('Juan', 'Historial');
    const deOperario = dialog.open.mock.calls[1][1].data as HistorialDialogData;

    expect(dialog.open.mock.calls[0][0]).toBe(HistorialDialog);
    expect(deUsuario.titulo).toBe('Historial de maria');
    deUsuario.cargar().subscribe();
    http.expectOne((r) => r.url.endsWith('/Usuarios/u2/historial') && r.method === 'GET').flush([]);
    deOperario.cargar().subscribe();
    http.expectOne((r) => r.url.endsWith('/Operarios/o1/historial') && r.method === 'GET').flush([]);
  });
});
