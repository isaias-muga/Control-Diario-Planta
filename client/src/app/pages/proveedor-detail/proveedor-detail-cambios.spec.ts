import { LOCALE_ID } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeEsAr from '@angular/common/locales/es-AR';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { HistorialDialog } from '../../components/historial-dialog/historial-dialog';
import { MotivoDialog, MotivoDialogData } from '../../components/motivo-dialog/motivo-dialog';
import { ListadoRecepciones } from '../../models/recepcion-listado';
import { Proveedor } from '../../models/proveedor';
import { cerrarSesionDePrueba, iniciarSesionDePrueba } from '../../testing/sesion';
import { Rol } from '../../utils/jwt';
import { ProveedorDetail } from './proveedor-detail';

registerLocaleData(localeEsAr);

const LIMITES = { acidezMaxima: 19, temperaturaMaxima: 20, aguaMaxima: 0, actualizadoEn: '2026-10-15T00:00:00Z' };

const vacio: ListadoRecepciones = {
  items: [],
  totales: {
    litros: 0, litrosReales: 0, cantidadRecepciones: 0, ticketMinimo: null, ticketMaximo: null,
    cantidadFueraDeRango: 0, fueraPorAcidez: 0, fueraPorTemperatura: 0, fueraPorAgua: 0,
  },
  dias: [],
  pagina: 1,
  tamanoPagina: 100,
  hayMas: false,
};

const activo: Proveedor = { id: 'test-id', nombre: 'Tambo Test', cantidadRecepciones: 3, activo: true, version: 'AAE=' };
const inactivo: Proveedor = { ...activo, activo: false };

// The Admin corrects the name and deactivates or reactivates (each with a reason, never a delete); everybody who reads can read the history
describe('ProveedorDetail: corrections, deactivation and history', () => {
  let fixture: ComponentFixture<ProveedorDetail>;
  let http: HttpTestingController;

  async function abrir(rol: Rol, proveedor: Proveedor): Promise<void> {
    iniciarSesionDePrueba(rol);
    await TestBed.configureTestingModule({
      imports: [ProveedorDetail],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), { provide: LOCALE_ID, useValue: 'es-AR' }],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ProveedorDetail);
    fixture.componentRef.setInput('id', 'test-id');
    await fixture.whenStable();
    http.expectOne((r) => r.url.endsWith('/Proveedores/test-id')).flush(proveedor);
    await fixture.whenStable();
    fixture.detectChanges();
    // the panel's own requests are not what these tests are about
    http.match((r) => r.url.endsWith('/Configuracion/calidad')).forEach((r) => r.flush(LIMITES));
    http.match((r) => r.url.endsWith('/Recepciones')).forEach((r) => r.flush(vacio));
  }

  afterEach(() => {
    vi.restoreAllMocks();
    cerrarSesionDePrueba();
  });

  const el = (): HTMLElement => fixture.nativeElement;
  const textos = (): string[] => Array.from(el().querySelectorAll('.page-header button, .page-header a')).map((b) => (b.textContent ?? '').trim());
  const boton = (nombre: string): HTMLButtonElement =>
    Array.from(el().querySelectorAll('.page-header button')).find((b) => (b.textContent ?? '').trim() === nombre) as HTMLButtonElement;

  function espiar(): { datos: () => MotivoDialogData | undefined; abierto: ReturnType<typeof vi.spyOn> } {
    let datos: MotivoDialogData | undefined;
    const abierto = vi.spyOn(TestBed.inject(MatDialog), 'open').mockImplementation(((_: unknown, config: { data: MotivoDialogData }) => {
      datos = config.data;
      return { afterClosed: () => of(true) };
    }) as never);
    return { datos: () => datos, abierto };
  }

  it('offers the Admin to correct the name, deactivate and read the history, besides registering', async () => {
    await abrir('Admin', activo);

    expect(textos()).toEqual(['Corregir nombre', 'Desactivar', 'Historial', 'Registrar recepción']);
  });

  it.each<Rol>(['Oficina', 'Consulta'])('gives %s only the history (and registering for the office)', async (rol) => {
    await abrir(rol, activo);

    expect(textos()).toEqual(rol === 'Oficina' ? ['Historial', 'Registrar recepción'] : ['Historial']);
  });

  it('marks an inactive proveedor, offers to reactivate it and no longer offers to register for it', async () => {
    await abrir('Admin', inactivo);

    expect(el().querySelector('h2')?.textContent).toBe('Tambo Test (inactivo)');
    expect(textos()).toEqual(['Corregir nombre', 'Reactivar', 'Historial']);
  });

  it('does not offer the office to register for an inactive proveedor either', async () => {
    await abrir('Oficina', inactivo);

    expect(textos()).toEqual(['Historial']);
  });

  it('asks for the reason and the new name, with the version that was read, when correcting the name', async () => {
    await abrir('Admin', activo);
    const { datos, abierto } = espiar();

    boton('Corregir nombre').click();

    expect(abierto.mock.calls[0][0]).toBe(MotivoDialog);
    expect(datos()?.nombre).toEqual({ etiqueta: 'Nombre', valor: 'Tambo Test' });
    datos()!.guardar('Mal escrito', 'Tambo Test S.A.').subscribe();
    const req = http.expectOne((r) => r.url.endsWith('/Proveedores/test-id') && r.method === 'PUT');
    expect(req.request.body).toEqual({ nombre: 'Tambo Test S.A.', motivo: 'Mal escrito', version: 'AAE=' });
    req.flush({ ...activo, nombre: 'Tambo Test S.A.' });
  });

  it('reloads the proveedor when the rename is refused because it changed meanwhile, so the next try has the new version', async () => {
    await abrir('Admin', activo);
    const { datos } = espiar();

    boton('Corregir nombre').click();
    datos()!.guardar('Mal escrito', 'Tambo Test S.A.').subscribe({ error: () => {} });
    http.expectOne((r) => r.url.endsWith('/Proveedores/test-id') && r.method === 'PUT').flush({ error: 'otro' }, { status: 409, statusText: 'Conflict' });
    // (the mocked dialog also "closes saved", so the page reloads twice here; both answers carry the new version)
    http.match((r) => r.url.endsWith('/Proveedores/test-id') && r.method === 'GET').filter((r) => !r.cancelled).forEach((r) => r.flush({ ...activo, version: 'NUEVA=' }));
    http.match((r) => r.url.endsWith('/Recepciones')).forEach((r) => r.flush(vacio));
    datos()!.guardar('Mal escrito', 'Tambo Test S.A.').subscribe();

    const req = http.expectOne((r) => r.url.endsWith('/Proveedores/test-id') && r.method === 'PUT');
    expect(req.request.body.version).toBe('NUEVA=');
    req.flush({ ...activo, nombre: 'Tambo Test S.A.' });
  });

  it('reloads the proveedor and the list after the name was corrected', async () => {
    await abrir('Admin', activo);
    espiar();

    boton('Corregir nombre').click();

    http.expectOne((r) => r.url.endsWith('/Proveedores/test-id') && r.method === 'GET').flush({ ...activo, nombre: 'Tambo Test S.A.' });
    http.match((r) => r.url.endsWith('/Recepciones')).forEach((r) => r.flush(vacio)); // the rows show the name
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el().querySelector('h2')?.textContent).toBe('Tambo Test S.A.');
  });

  it('asks for the reason and deactivates (a red action that keeps all the history) through the API', async () => {
    await abrir('Admin', activo);
    const { datos } = espiar();

    boton('Desactivar').click();

    expect(datos()?.titulo).toBe('Desactivar proveedor');
    expect(datos()?.peligro).toBe(true);
    expect(datos()?.mensaje).toContain('Sus recepciones y su historial se conservan');
    datos()!.guardar('Cerró el tambo', null).subscribe();
    const req = http.expectOne((r) => r.url.endsWith('/Proveedores/test-id/desactivar'));
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ motivo: 'Cerró el tambo' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne((r) => r.url.endsWith('/Proveedores/test-id') && r.method === 'GET').flush(inactivo);
    http.match((r) => r.url.endsWith('/Recepciones')).forEach((r) => r.flush(vacio));
  });

  it('reactivates an inactive proveedor with a reason', async () => {
    await abrir('Admin', inactivo);
    const { datos } = espiar();

    boton('Reactivar').click();

    expect(datos()?.titulo).toBe('Reactivar proveedor');
    expect(datos()?.peligro).toBeUndefined();
    datos()!.guardar('Volvió a entregar', null).subscribe();
    const req = http.expectOne((r) => r.url.endsWith('/Proveedores/test-id/reactivar'));
    expect(req.request.body).toEqual({ motivo: 'Volvió a entregar' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne((r) => r.url.endsWith('/Proveedores/test-id') && r.method === 'GET').flush(activo);
    http.match((r) => r.url.endsWith('/Recepciones')).forEach((r) => r.flush(vacio));
  });

  it('opens the history of the proveedor', async () => {
    await abrir('Consulta', activo);
    const abierto = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(undefined) } as never);

    boton('Historial').click();

    const [componente, config] = abierto.mock.calls[0] as unknown as [unknown, { data: { titulo: string; contexto: string; cargar: () => { subscribe: () => void } } }];
    expect(componente).toBe(HistorialDialog);
    expect(config.data.titulo).toBe('Historial del proveedor');
    expect(config.data.contexto).toBe('Tambo Test');
    config.data.cargar().subscribe();
    http.expectOne((r) => r.url.endsWith('/Proveedores/test-id/historial')).flush([]);
  });
});
