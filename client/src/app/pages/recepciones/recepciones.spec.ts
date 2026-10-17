import { LOCALE_ID } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeEsAr from '@angular/common/locales/es-AR';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { ListadoRecepciones } from '../../models/recepcion-listado';
import { RegistrarRecepcionDialog } from '../../components/registrar-recepcion-dialog/registrar-recepcion-dialog';
import { Recepciones } from './recepciones';

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

describe('Recepciones page', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: LOCALE_ID, useValue: 'es-AR' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    http.verify();
  });

  async function abrir(url = '/'): Promise<void> {
    TestBed.inject(Router).resetConfig([{ path: '', component: Recepciones }]);
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, Recepciones);
    harness.detectChanges();
    // the page and the panel both read the proveedores; the panel also reads the limits and the list
    http.match((r) => r.url.endsWith('/Proveedores')).forEach((r) =>
      r.flush([{ id: 'p1', nombre: 'Tambo Don Pedro', cantidadRecepciones: 3 }]),
    );
    http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush(LIMITES);
    http.expectOne((r) => r.url.endsWith('/Recepciones')).flush(vacio);
    await harness.fixture.whenStable();
    harness.detectChanges();
  }

  const el = (): HTMLElement => harness.routeNativeElement as HTMLElement;
  const texto = (selector: string): string => (el().querySelector(selector)?.textContent ?? '').replace(/\s+/g, ' ').trim();

  it('shows the title and the period and proveedor of the filters as subtitle', async () => {
    await abrir('/?desde=2026-10-14&hasta=2026-10-15');

    expect(texto('h2')).toBe('Recepciones');
    expect(texto('.subtitulo')).toBe('14/10/2026 – 15/10/2026 · todos los proveedores');
  });

  it('shows a single date for a single day', async () => {
    await abrir('/?desde=2026-10-14&hasta=2026-10-14');

    expect(texto('.subtitulo')).toBe('14/10/2026 · todos los proveedores');
  });

  it('shows the name of the filtered proveedor in the subtitle', async () => {
    await abrir('/?desde=2026-10-14&hasta=2026-10-14&proveedor=p1');

    expect(texto('.subtitulo')).toBe('14/10/2026 · Tambo Don Pedro');
  });

  it('opens the registration dialog without a proveedor (it is chosen in the form)', async () => {
    const abrirDialogo = vi.spyOn(RegistrarRecepcionDialog, 'abrir').mockReturnValue(of(false));
    await abrir();

    (el().querySelector('.encabezado button') as HTMLButtonElement).click();

    expect(texto('.encabezado button')).toBe('+ Registrar recepción');
    expect(abrirDialogo).toHaveBeenCalledExactlyOnceWith(expect.anything(), {});
  });

  it('links to the quick reception flow', async () => {
    await abrir();

    const enlace = el().querySelector('.encabezado a') as HTMLAnchorElement;

    expect(texto('.encabezado a')).toBe('Carga rápida');
    expect(enlace.getAttribute('href')).toBe('/recepciones/carga');
  });

  it('reloads the list after something was registered', async () => {
    vi.spyOn(RegistrarRecepcionDialog, 'abrir').mockReturnValue(of(true));
    await abrir();

    (el().querySelector('.encabezado button') as HTMLButtonElement).click();

    http.expectOne((r) => r.url.endsWith('/Recepciones')).flush(vacio);
  });

  it('does not reload when the dialog is closed without registering', async () => {
    vi.spyOn(RegistrarRecepcionDialog, 'abrir').mockReturnValue(of(false));
    await abrir();

    (el().querySelector('.encabezado button') as HTMLButtonElement).click();

    http.expectNone((r) => r.url.endsWith('/Recepciones'));
  });
});
