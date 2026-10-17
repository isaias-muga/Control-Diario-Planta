import { LOCALE_ID } from '@angular/core';
import { cerrarSesionDePrueba, iniciarSesionDePrueba } from '../../testing/sesion';
import { registerLocaleData } from '@angular/common';
import localeEsAr from '@angular/common/locales/es-AR';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { ProveedorDetail } from './proveedor-detail';
import { RecepcionesPanel } from '../../components/recepciones-panel/recepciones-panel';
import { RegistrarRecepcionDialog } from '../../components/registrar-recepcion-dialog/registrar-recepcion-dialog';
import { ListadoRecepciones } from '../../models/recepcion-listado';
import { Proveedor } from '../../models/proveedor';
import { hoyEnPlanta, rangoDeAtajo } from '../../utils/periodos';

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

const proveedores: Proveedor[] = [
  { id: 'otro', nombre: 'Tambo Otro', cantidadRecepciones: 1 },
  { id: 'test-id', nombre: 'Tambo Test', cantidadRecepciones: 3 },
];

describe('ProveedorDetail', () => {
  let component: ProveedorDetail;
  let fixture: ComponentFixture<ProveedorDetail>;
  let http: HttpTestingController;

  beforeEach(async () => {
    iniciarSesionDePrueba('Admin');
    await TestBed.configureTestingModule({
      imports: [ProveedorDetail],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: LOCALE_ID, useValue: 'es-AR' },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ProveedorDetail);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('id', 'test-id');
    await fixture.whenStable();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    http.verify();
    cerrarSesionDePrueba();
  });

  /** Answers the proveedor request and, once the page shows the panel, the panel's own requests. */
  async function cargar(proveedor: Proveedor = proveedores[1]): Promise<void> {
    http.expectOne((r) => r.url.endsWith(`/Proveedores/${component.id()}`)).flush(proveedor);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function cargarPanel(): Promise<void> {
    await cargar();
    http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush(LIMITES);
    http.expectOne((r) => r.url.endsWith('/Recepciones')).flush(vacio);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const el = (): HTMLElement => fixture.nativeElement;

  it('should create', () => {
    http.expectOne((r) => r.url.endsWith('/Proveedores/test-id'));
    expect(component).toBeTruthy();
  });

  it('shows the proveedor name and the registration button', async () => {
    await cargarPanel();

    expect(el().querySelector('h2')?.textContent).toBe('Tambo Test');
    expect(el().querySelector('.page-header button')?.textContent?.trim()).toBe('Registrar recepción');
  });

  it('asks for this proveedor only, not for the whole list', async () => {
    await cargarPanel();

    http.expectNone((r) => r.url.endsWith('/Proveedores'));
  });

  it('shows the reusable list filtered by this proveedor, from the current month', async () => {
    await fixture.whenStable();
    http.expectOne((r) => r.url.endsWith('/Proveedores/test-id')).flush(proveedores[1]);
    await fixture.whenStable();
    fixture.detectChanges();
    http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush(LIMITES);

    const req = http.expectOne((r) => r.url.endsWith('/Recepciones'));

    const mes = rangoDeAtajo('mes', hoyEnPlanta());
    expect(req.request.params.get('proveedorId')).toBe('test-id');
    expect(req.request.params.get('desde')).toBe(mes.desde);
    expect(req.request.params.get('hasta')).toBe(mes.hasta);
    req.flush(vacio);
    fixture.detectChanges();
    const panel = fixture.debugElement.query(By.directive(RecepcionesPanel)).componentInstance as RecepcionesPanel;
    expect(panel.proveedorFijo()).toBe('test-id');
  });

  it('shows "Proveedor no encontrado." when the proveedor does not exist', async () => {
    http.expectOne((r) => r.url.endsWith('/Proveedores/test-id')).flush({}, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el().querySelector('.state-message')?.textContent).toContain('Proveedor no encontrado.');
    expect(el().querySelector('app-recepciones-panel')).toBeNull();
  });

  it('shows "Proveedor no encontrado." when the id in the URL is malformed (400)', async () => {
    http.expectOne((r) => r.url.endsWith('/Proveedores/test-id')).flush({}, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el().querySelector('.state-message')?.textContent).toContain('Proveedor no encontrado.');
  });

  it('shows an error when the proveedor cannot be loaded', async () => {
    http.expectOne((r) => r.url.endsWith('/Proveedores/test-id')).flush({}, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el().querySelector('.form-error')?.textContent).toContain('No se pudo cargar el proveedor.');
  });

  it('opens the registration dialog for this proveedor', async () => {
    const abrir = vi.spyOn(RegistrarRecepcionDialog, 'abrir').mockReturnValue(of(false));
    await cargarPanel();

    (el().querySelector('.page-header button') as HTMLButtonElement).click();

    expect(abrir).toHaveBeenCalledExactlyOnceWith(TestBed.inject(MatDialog), { proveedorId: 'test-id' });
  });

  it('reloads the list when something was registered', async () => {
    vi.spyOn(RegistrarRecepcionDialog, 'abrir').mockReturnValue(of(true));
    await cargarPanel();

    (el().querySelector('.page-header button') as HTMLButtonElement).click();

    http.expectOne((r) => r.url.endsWith('/Recepciones')).flush(vacio);
  });

  it('does not reload the list when the dialog is closed without registering', async () => {
    vi.spyOn(RegistrarRecepcionDialog, 'abrir').mockReturnValue(of(false));
    await cargarPanel();

    (el().querySelector('.page-header button') as HTMLButtonElement).click();

    http.expectNone((r) => r.url.endsWith('/Recepciones'));
  });

  it('loads again when the route id changes', async () => {
    await cargarPanel();

    fixture.componentRef.setInput('id', 'otro');
    await fixture.whenStable();
    http.expectOne((r) => r.url.endsWith('/Proveedores/otro')).flush(proveedores[0]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el().querySelector('h2')?.textContent).toBe('Tambo Otro');
    http.match((r) => r.url.endsWith('/Recepciones')).forEach((r) => r.flush(vacio));
    http.match((r) => r.url.endsWith('/Configuracion/calidad')).forEach((r) => r.flush(LIMITES));
  });

  it('offers no way to register a recepción to the office (read only)', async () => {
    TestBed.resetTestingModule();
    cerrarSesionDePrueba();
    iniciarSesionDePrueba('Oficina');
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), { provide: LOCALE_ID, useValue: 'es-AR' }],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ProveedorDetail);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('id', 'test-id');
    await fixture.whenStable();

    http.expectOne((r) => r.url.endsWith(`/Proveedores/${component.id()}`)).flush(proveedores[1]);
    await fixture.whenStable();
    fixture.detectChanges();
    http.match(() => true); // the panel's own requests are not what this test is about

    expect(el().querySelector('h2')?.textContent).toBe('Tambo Test');
    expect(el().querySelector('.page-header button')).toBeNull();
  });
});
