import { LOCALE_ID } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeEsAr from '@angular/common/locales/es-AR';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { MatTooltip } from '@angular/material/tooltip';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { RecepcionListado } from '../../models/recepcion-listado';
import { cerrarSesionDePrueba, iniciarSesionDePrueba } from '../../testing/sesion';
import { Rol } from '../../utils/jwt';
import { HistorialDialog } from '../historial-dialog/historial-dialog';
import { MotivoDialog, MotivoDialogData } from '../motivo-dialog/motivo-dialog';
import { RecepcionesList } from './recepciones-list';

registerLocaleData(localeEsAr);

const recepcion = (cambios: Partial<RecepcionListado>): RecepcionListado => ({
  id: 'r1',
  ticket: 4980,
  fechaHora: '2026-10-15T09:26:00',
  proveedorId: 'p1',
  proveedorNombre: 'Tambo Don Pedro',
  litros: 1214,
  aguaPorcentaje: null,
  litrosReales: 1214,
  grasa: null,
  acidez: 18,
  temperatura: 5.8,
  silo: 1,
  observaciones: null,
  registradoPor: null,
  anulada: false,
  motivoAnulacion: null,
  anuladaPor: null,
  anuladaEn: null,
  cantidadCorrecciones: 0,
  motivos: [],
  ...cambios,
});

// What the list offers for corrections and voids: the row menu is for the Admin only; the "Anulada" and "Corregida" marks open the history for everybody
describe('RecepcionesList: corrections, voids and history', () => {
  let fixture: ComponentFixture<RecepcionesList>;
  let http: HttpTestingController;

  async function abrir(rol: Rol | null, items: RecepcionListado[]): Promise<void> {
    if (rol) {
      iniciarSesionDePrueba(rol);
    }
    await TestBed.configureTestingModule({
      imports: [RecepcionesList],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: LOCALE_ID, useValue: 'es-AR' },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RecepcionesList);
    fixture.componentRef.setInput('items', items);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => {
    vi.restoreAllMocks();
    TestBed.inject(MatDialog).closeAll();
    cerrarSesionDePrueba();
  });

  const encabezados = (): string[] =>
    fixture.debugElement.queryAll(By.css('th')).map((th) => (th.nativeElement.textContent as string).trim());
  const menus = (): HTMLButtonElement[] => Array.from(fixture.nativeElement.querySelectorAll('td.acciones button.menu-fila'));
  const itemsDelMenu = (): string[] =>
    Array.from(document.querySelectorAll('.mat-mdc-menu-panel [role="menuitem"]')).map((e) => (e.textContent ?? '').trim());

  async function abrirMenu(indice = 0): Promise<void> {
    menus()[indice].click();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('gives the Admin an Acciones column with one menu button per row, with tooltip and aria-label', async () => {
    await abrir('Admin', [recepcion({ id: 'a', ticket: 4980 }), recepcion({ id: 'b', ticket: 4981 })]);

    expect(encabezados().at(-1)).toBe('Acc.');
    const cabecera = fixture.debugElement.queryAll(By.css('th')).at(-1)!;
    expect(cabecera.nativeElement.getAttribute('aria-label')).toBe('Acciones');
    expect(cabecera.injector.get(MatTooltip).message).toBe('Acciones');
    expect(menus()).toHaveLength(2);
    expect(menus()[0].getAttribute('aria-label')).toBe('Acciones de la recepción del ticket 4980');
  });

  it.each<Rol | null>(['Oficina', 'Consulta', null])('gives %s no Acciones column and no menu', async (rol) => {
    await abrir(rol, [recepcion({})]);

    expect(encabezados()).not.toContain('Acc.');
    expect(menus()).toHaveLength(0);
  });

  it('offers Corregir (a link to the correction page), Historial and Anular in the menu of a valid recepción', async () => {
    await abrir('Admin', [recepcion({ id: 'r9' })]);

    await abrirMenu();

    expect(itemsDelMenu()).toEqual(['Corregir', 'Historial', 'Anular']);
    const corregir = document.querySelector('.mat-mdc-menu-panel a[role="menuitem"]') as HTMLAnchorElement;
    expect(corregir.getAttribute('href')).toBe('/recepciones/r9/corregir');
  });

  it('offers only Historial for a voided recepción', async () => {
    await abrir('Admin', [recepcion({ anulada: true, motivoAnulacion: 'Se cargó dos veces' })]);

    await abrirMenu();

    expect(itemsDelMenu()).toEqual(['Historial']);
  });

  it('shows a voided recepción as Anulada, struck through, with who, when and why in the tooltip', async () => {
    await abrir('Consulta', [
      recepcion({ anulada: true, motivoAnulacion: 'Se cargó dos veces', anuladaPor: 'Ana Admin', anuladaEn: '2026-10-15T15:30:00' }),
    ]);

    const fila = fixture.nativeElement.querySelector('tr.mat-mdc-row:not(.fila-dia)') as HTMLElement;
    const marca = fixture.debugElement.query(By.css('button.estado-anulada'));
    expect(fila.classList).toContain('fila-anulada');
    expect(marca.nativeElement.textContent.trim()).toBe('Anulada');
    expect(marca.injector.get(MatTooltip).message).toBe('Anulada por Ana Admin el 15/10/2026 12:30. Motivo: Se cargó dos veces');
    expect(fila.querySelector('.estado-ok')).toBeNull();
  });

  it('shows no mark on a recepción that was never corrected and the number of corrections on one that was', async () => {
    await abrir('Oficina', [
      recepcion({ id: 'a', cantidadCorrecciones: 0 }),
      recepcion({ id: 'b', cantidadCorrecciones: 1 }),
      recepcion({ id: 'c', cantidadCorrecciones: 3 }),
    ]);

    const marcas = fixture.debugElement.queryAll(By.css('button.marca-corregida'));
    expect(marcas).toHaveLength(2);
    expect(marcas[0].nativeElement.getAttribute('aria-label')).toBe('Corregida 1 vez. Ver historial');
    expect(marcas[1].nativeElement.getAttribute('aria-label')).toBe('Corregida 3 veces. Ver historial');
  });

  it('opens the history of the recepción when the Corregida mark is clicked, for any role that reads', async () => {
    await abrir('Consulta', [recepcion({ id: 'r7', cantidadCorrecciones: 2, ticket: 4901, proveedorNombre: 'Tambo Test' })]);
    const abierto = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(undefined) } as never);

    (fixture.nativeElement.querySelector('button.marca-corregida') as HTMLButtonElement).click();

    expect(abierto).toHaveBeenCalledTimes(1);
    const [componente, config] = abierto.mock.calls[0] as unknown as [unknown, { data: { titulo: string; contexto: string; cargar: () => { subscribe: () => void } } }];
    expect(componente).toBe(HistorialDialog);
    expect(config.data.titulo).toBe('Historial de la recepción');
    expect(config.data.contexto).toBe('Ticket 4901 · Tambo Test · 15/10/2026 09:26');
    config.data.cargar().subscribe();
    http.expectOne((r) => r.url.endsWith('/Recepciones/r7/historial')).flush([]);
  });

  it('opens the history when the Anulada mark is clicked', async () => {
    await abrir('Oficina', [recepcion({ anulada: true })]);
    const abierto = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(undefined) } as never);

    (fixture.nativeElement.querySelector('button.estado-anulada') as HTMLButtonElement).click();

    expect(abierto.mock.calls[0][0]).toBe(HistorialDialog);
  });

  it('asks for the reason, voids through the API and tells the page to reload when Anular is confirmed', async () => {
    await abrir('Admin', [recepcion({ id: 'r5', ticket: 4980 })]);
    const recargas: unknown[] = [];
    fixture.componentInstance.cambio.subscribe(() => recargas.push(1));
    let datos: MotivoDialogData | undefined;
    const abierto = vi.spyOn(TestBed.inject(MatDialog), 'open').mockImplementation(((_: unknown, config: { data: MotivoDialogData }) => {
      datos = config.data;
      return { afterClosed: () => of(true) };
    }) as never);

    await abrirMenu();
    (Array.from(document.querySelectorAll('.mat-mdc-menu-panel [role="menuitem"]')).find((e) => e.textContent?.trim() === 'Anular') as HTMLElement).click();

    expect(abierto.mock.calls[0][0]).toBe(MotivoDialog);
    expect(datos?.titulo).toBe('Anular recepción');
    expect(datos?.peligro).toBe(true);
    expect(datos?.mensaje).toContain('no se puede volver a usar');
    datos!.guardar('Se cargó dos veces', null).subscribe();
    const req = http.expectOne((r) => r.url.endsWith('/Recepciones/r5/anular'));
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ motivo: 'Se cargó dos veces' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    expect(recargas).toHaveLength(1);
  });

  it('does not reload when the Anular dialog is cancelled', async () => {
    await abrir('Admin', [recepcion({})]);
    const recargas: unknown[] = [];
    fixture.componentInstance.cambio.subscribe(() => recargas.push(1));
    vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(undefined) } as never);

    await abrirMenu();
    (Array.from(document.querySelectorAll('.mat-mdc-menu-panel [role="menuitem"]')).find((e) => e.textContent?.trim() === 'Anular') as HTMLElement).click();

    expect(recargas).toHaveLength(0);
  });
});
