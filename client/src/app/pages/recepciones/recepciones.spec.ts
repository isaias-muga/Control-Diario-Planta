import { LOCALE_ID } from '@angular/core';
import { cerrarSesionDePrueba, iniciarSesionDePrueba } from '../../testing/sesion';
import { registerLocaleData } from '@angular/common';
import localeEsAr from '@angular/common/locales/es-AR';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ListadoRecepciones } from '../../models/recepcion-listado';
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
    iniciarSesionDePrueba('Admin');
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
    cerrarSesionDePrueba();
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

  it.each(['Admin', 'Oficina'] as const)('offers one "+ Registrar recepción" link to the registration flow to %s', async (rol) => {
    TestBed.resetTestingModule();
    cerrarSesionDePrueba();
    iniciarSesionDePrueba(rol);
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), { provide: LOCALE_ID, useValue: 'es-AR' }],
    });
    http = TestBed.inject(HttpTestingController);

    await abrir();

    const botones = Array.from(el().querySelectorAll('.encabezado button, .encabezado a'));
    expect(botones.map((b) => b.textContent?.trim())).toEqual(['+ Registrar recepción']);
    expect(botones[0].getAttribute('href')).toBe('/recepciones/carga');
  });

  it('has no registration dialog and no "Carga rápida" duplicate', async () => {
    await abrir();

    expect(el().textContent).not.toContain('Carga rápida');
  });

});
