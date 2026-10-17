import { Component, LOCALE_ID } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeEsAr from '@angular/common/locales/es-AR';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ListadoRecepciones, RecepcionListado, TotalesListado } from '../../models/recepcion-listado';
import { hoyEnPlanta, rangoDeAtajo } from '../../utils/periodos';
import { RecepcionesFiltros } from '../recepciones-filtros/recepciones-filtros';
import { RecepcionesPanel } from './recepciones-panel';

registerLocaleData(localeEsAr);

@Component({ imports: [RecepcionesPanel], template: '<app-recepciones-panel />' })
class PaginaTodos {}

@Component({
  imports: [RecepcionesPanel],
  template: '<app-recepciones-panel proveedorFijo="p-fijo" atajoInicial="mes" />',
})
class PaginaProveedor {}

const recepcion = (cambios: Partial<RecepcionListado>): RecepcionListado => ({
  id: 'r',
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
  motivos: [],
  ...cambios,
});

const totales = (cambios: Partial<TotalesListado> = {}): TotalesListado => ({
  litros: 25069,
  litrosReales: 25040,
  cantidadRecepciones: 12,
  ticketMinimo: 4969,
  ticketMaximo: 4980,
  cantidadFueraDeRango: 3,
  fueraPorAcidez: 1,
  fueraPorTemperatura: 1,
  fueraPorAgua: 1,
  ...cambios,
});

const listado = (cambios: Partial<ListadoRecepciones> = {}): ListadoRecepciones => ({
  items: [recepcion({ id: 'a' })],
  totales: totales(),
  dias: [{ fecha: '2026-10-15', cantidadRecepciones: 12, litros: 25069, cantidadFueraDeRango: 3 }],
  pagina: 1,
  tamanoPagina: 100,
  hayMas: false,
  ...cambios,
});

const LIMITES = { acidezMaxima: 19, temperaturaMaxima: 20, aguaMaxima: 0, actualizadoEn: '2026-10-15T00:00:00Z' };

describe('RecepcionesPanel', () => {
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

  afterEach(() => http.verify());

  async function abrir(url: string, pagina: typeof PaginaTodos | typeof PaginaProveedor = PaginaTodos): Promise<void> {
    TestBed.inject(Router).resetConfig([{ path: '', component: pagina }]);
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, pagina);
    harness.detectChanges();
  }

  const esListado = (r: TestRequest['request']): boolean => r.url.endsWith('/Recepciones');
  const pedidoListado = (): TestRequest => http.expectOne((r) => esListado(r));

  async function cargar(datos: ListadoRecepciones = listado(), proveedores = true): Promise<void> {
    http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush(LIMITES);
    if (proveedores) {
      http.expectOne((r) => r.url.endsWith('/Proveedores')).flush([{ id: 'p1', nombre: 'Tambo Don Pedro', cantidadRecepciones: 3 }]);
    }
    pedidoListado().flush(datos);
    await harness.fixture.whenStable();
    harness.detectChanges();
  }

  const texto = (selector: string): string =>
    ((harness.routeNativeElement as HTMLElement).querySelector(selector)?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const elemento = (): HTMLElement => harness.routeNativeElement as HTMLElement;

  describe('filters from the URL', () => {
    it('lists today by default on the page that has no fixed proveedor', async () => {
      await abrir('/');
      http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush(LIMITES);
      http.expectOne((r) => r.url.endsWith('/Proveedores')).flush([]);

      const req = pedidoListado();

      const hoy = hoyEnPlanta();
      expect(req.request.params.get('desde')).toBe(hoy);
      expect(req.request.params.get('hasta')).toBe(hoy);
      expect(req.request.params.has('proveedorId')).toBe(false);
      expect(req.request.params.has('ticket')).toBe(false);
      expect(req.request.params.has('soloFueraDeRango')).toBe(false);
      expect(req.request.params.has('pagina')).toBe(false);
      req.flush(listado());
    });

    it('sends every filter of the query params', async () => {
      await abrir('/?desde=2026-10-01&hasta=2026-10-15&proveedor=p1&ticket=4980&fuera=1');
      http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush(LIMITES);
      http.expectOne((r) => r.url.endsWith('/Proveedores')).flush([]);

      const req = pedidoListado();

      expect(req.request.params.get('desde')).toBe('2026-10-01');
      expect(req.request.params.get('hasta')).toBe('2026-10-15');
      expect(req.request.params.get('proveedorId')).toBe('p1');
      expect(req.request.params.get('ticket')).toBe('4980');
      expect(req.request.params.get('soloFueraDeRango')).toBe('true');
      req.flush(listado());
    });

    it.each([
      ['/?desde=abc&hasta=2026-10-15'],
      ['/?desde=2026-02-30&hasta=2026-03-01'],
      ['/?desde=2026-10-01'],
      ['/?desde=2026-10-01&hasta=DROP TABLE'],
    ])('falls back to the default period for the malformed dates in %s', async (url) => {
      await abrir(url);
      http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush(LIMITES);
      http.expectOne((r) => r.url.endsWith('/Proveedores')).flush([]);

      const req = pedidoListado();

      expect(req.request.params.get('desde')).toBe(hoyEnPlanta());
      expect(req.request.params.get('hasta')).toBe(hoyEnPlanta());
      req.flush(listado());
    });

    it.each([['abc'], ['-5'], ['1.5'], ['4980; DROP']])('ignores the malformed ticket "%s"', async (ticket) => {
      await abrir(`/?ticket=${encodeURIComponent(ticket)}`);
      http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush(LIMITES);
      http.expectOne((r) => r.url.endsWith('/Proveedores')).flush([]);

      const req = pedidoListado();

      expect(req.request.params.has('ticket')).toBe(false);
      req.flush(listado());
    });

    it('uses the period of atajoInicial when the URL does not say one', async () => {
      await abrir('/', PaginaProveedor);
      http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush(LIMITES);

      const req = pedidoListado();

      const mes = rangoDeAtajo('mes', hoyEnPlanta());
      expect(req.request.params.get('desde')).toBe(mes.desde);
      expect(req.request.params.get('hasta')).toBe(mes.hasta);
      req.flush(listado());
    });

    it('fixes the proveedor, ignoring the proveedor in the URL, and does not load the proveedores', async () => {
      await abrir('/?proveedor=otro', PaginaProveedor);
      http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush(LIMITES);

      const req = pedidoListado();

      expect(req.request.params.get('proveedorId')).toBe('p-fijo');
      http.expectNone((r) => r.url.endsWith('/Proveedores'));
      req.flush(listado());
      harness.detectChanges();
      expect(elemento().querySelector('.campo-proveedor')).toBeNull();
    });

    it('shows the proveedor selector when the proveedor is not fixed', async () => {
      await abrir('/');
      await cargar();

      expect(elemento().querySelector('.campo-proveedor')).toBeTruthy();
    });
  });

  describe('changing filters', () => {
    const filtros = () => harness.fixture.debugElement.query(By.directive(RecepcionesFiltros)).componentInstance as RecepcionesFiltros;

    it('writes every filter to the URL and loads again', async () => {
      await abrir('/');
      await cargar();

      filtros().cambio.emit({
        desde: '2026-10-01', hasta: '2026-10-15', proveedorId: 'p1', ticket: 4980, soloFueraDeRango: true,
      });
      await harness.fixture.whenStable();
      harness.detectChanges();

      const url = TestBed.inject(Router).url;
      expect(url).toContain('desde=2026-10-01');
      expect(url).toContain('hasta=2026-10-15');
      expect(url).toContain('proveedor=p1');
      expect(url).toContain('ticket=4980');
      expect(url).toContain('fuera=1');
      const req = pedidoListado();
      expect(req.request.params.get('soloFueraDeRango')).toBe('true');
      req.flush(listado());
    });

    it('keeps the URL free of filters that are off', async () => {
      await abrir('/?ticket=4980&fuera=1&proveedor=p1');
      await cargar();

      filtros().cambio.emit({ desde: '2026-10-14', hasta: '2026-10-14', proveedorId: null, ticket: null, soloFueraDeRango: false });
      await harness.fixture.whenStable();
      harness.detectChanges();

      const url = TestBed.inject(Router).url;
      expect(url).toBe('/?desde=2026-10-14&hasta=2026-10-14');
      pedidoListado().flush(listado());
    });

    it('does not write the proveedor to the URL where it is fixed', async () => {
      await abrir('/', PaginaProveedor);
      http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush(LIMITES);
      pedidoListado().flush(listado());
      harness.detectChanges();

      filtros().cambio.emit({ desde: '2026-10-14', hasta: '2026-10-14', proveedorId: 'p-fijo', ticket: null });
      await harness.fixture.whenStable();
      harness.detectChanges();

      expect(TestBed.inject(Router).url).not.toContain('proveedor=');
      pedidoListado().flush(listado());
    });

    it('goes back to the first page when a filter changes', async () => {
      await abrir('/');
      await cargar(listado({ hayMas: true }));
      (elemento().querySelector('.cargar-mas button') as HTMLButtonElement).click();
      http.expectOne((r) => esListado(r) && r.params.get('pagina') === '2').flush(listado({ pagina: 2 }));

      filtros().cambio.emit({ desde: '2026-10-14', hasta: '2026-10-14', proveedorId: null, ticket: 1 });
      await harness.fixture.whenStable();
      harness.detectChanges();

      expect(pedidoListado().request.params.has('pagina')).toBe(false);
      http.match((r) => esListado(r));
    });
  });

  describe('totals', () => {
    it('shows liters received and real, the ticket range and the out-of-range count by reason', async () => {
      await abrir('/');
      await cargar();

      const tarjetas = Array.from(elemento().querySelectorAll('.tarjetas mat-card')).map((c) =>
        Array.from(c.querySelectorAll('span'))
          .map((span) => (span.textContent ?? '').replace(/\s+/g, ' ').trim())
          .filter(Boolean)
          .join(' '),
      );

      expect(tarjetas).toEqual([
        'Litros recibidos 25.069',
        'Litros reales 25.040 −29 L por agua',
        'Recepciones 12 tickets 4969 – 4980',
        'Fuera de rango 3 1 temperatura · 1 acidez · 1 agua',
      ]);
    });

    it('highlights the out-of-range card only when there are out-of-range recepciones', async () => {
      await abrir('/');
      await cargar(listado({ totales: totales({ cantidadFueraDeRango: 0, fueraPorAcidez: 0, fueraPorTemperatura: 0, fueraPorAgua: 0 }) }));

      const tarjeta = elemento().querySelectorAll('.tarjetas mat-card')[3] as HTMLElement;

      expect(tarjeta.classList.contains('fuera-de-rango')).toBe(false);
      expect(tarjeta.textContent).toContain('sin desvíos');
    });

    it('omits the water note and the ticket range when there is nothing to say', async () => {
      await abrir('/');
      await cargar(listado({ totales: totales({ litros: 100, litrosReales: 100, ticketMinimo: null, ticketMaximo: null }) }));

      const tarjetas = elemento().querySelectorAll('.tarjetas mat-card');

      expect(tarjetas[1].textContent).not.toContain('por agua');
      expect(tarjetas[2].textContent).not.toContain('tickets');
    });
  });

  describe('list', () => {
    it('shows the recepciones with the day subtotals and the footer with the current limits', async () => {
      await abrir('/');
      await cargar();

      expect(elemento().querySelectorAll('tr.mat-mdc-row:not(.fila-dia)')).toHaveLength(1);
      expect(texto('tr.fila-dia')).toContain('12 recepciones');
      expect(texto('.pie')).toContain('Límites: acidez > 19 °D · temperatura > 20 °C · agua > 0 %');
      expect(texto('.pie')).toContain('1 de 12 recepciones');
      expect(elemento().querySelector('.pie a')!.getAttribute('href')).toBe('/configuracion');
    });

    it('hides the proveedor column where the proveedor is fixed', async () => {
      await abrir('/', PaginaProveedor);
      http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush(LIMITES);
      pedidoListado().flush(listado());
      harness.detectChanges();

      expect(elemento().querySelector('a.proveedor-link')).toBeNull();
    });

    it('shows a message when no recepción matches the filters', async () => {
      await abrir('/');
      await cargar(listado({ items: [], dias: [], totales: totales({ cantidadRecepciones: 0, ticketMinimo: null, ticketMaximo: null }) }));

      expect(texto('.state-message')).toBe('No hay recepciones con estos filtros.');
      expect(elemento().querySelector('app-recepciones-list')).toBeNull();
    });

    it('shows the API error message and no list when the request fails', async () => {
      await abrir('/');
      http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush(LIMITES);
      http.expectOne((r) => r.url.endsWith('/Proveedores')).flush([]);
      pedidoListado().flush({ error: 'El período no puede superar los 366 días.' }, { status: 400, statusText: 'Bad Request' });
      harness.detectChanges();

      expect(texto('.form-error')).toBe('El período no puede superar los 366 días.');
      expect(elemento().querySelector('app-recepciones-list')).toBeNull();
    });

    it('still shows the list when the limits cannot be loaded, without the footer limits', async () => {
      await abrir('/');
      http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush({}, { status: 500, statusText: 'Server Error' });
      http.expectOne((r) => r.url.endsWith('/Proveedores')).flush([]);
      pedidoListado().flush(listado());
      harness.detectChanges();

      expect(elemento().querySelectorAll('tr.mat-mdc-row:not(.fila-dia)')).toHaveLength(1);
      expect(texto('.limites')).toBe('');
    });
  });

  describe('Cargar más', () => {
    it('asks for the next page, appends its recepciones and replaces the subtotals of the days it repeats', async () => {
      await abrir('/?desde=2026-10-14&hasta=2026-10-15');
      await cargar(
        listado({
          items: [recepcion({ id: 'a', ticket: 4980 })],
          dias: [{ fecha: '2026-10-15', cantidadRecepciones: 2, litros: 2000, cantidadFueraDeRango: 0 }],
          hayMas: true,
        }),
      );

      (elemento().querySelector('.cargar-mas button') as HTMLButtonElement).click();
      const req = http.expectOne((r) => esListado(r) && r.params.get('pagina') === '2');
      expect(req.request.params.get('desde')).toBe('2026-10-14');
      req.flush(
        listado({
          items: [
            recepcion({ id: 'b', ticket: 4979, fechaHora: '2026-10-15T09:02:00' }),
            recepcion({ id: 'c', ticket: 4974, fechaHora: '2026-10-14T09:50:00' }),
          ],
          dias: [
            { fecha: '2026-10-15', cantidadRecepciones: 2, litros: 2000, cantidadFueraDeRango: 0 },
            { fecha: '2026-10-14', cantidadRecepciones: 1, litros: 900, cantidadFueraDeRango: 0 },
          ],
          pagina: 2,
          hayMas: false,
        }),
      );
      harness.detectChanges();

      const tickets = Array.from(elemento().querySelectorAll('tr.mat-mdc-row:not(.fila-dia) td:nth-child(2)')).map((td) => td.textContent?.trim());
      expect(tickets).toEqual(['4980', '4979', '4974']);
      expect(Array.from(elemento().querySelectorAll('tr.fila-dia')).map((tr) => (tr.textContent ?? '').replace(/\s+/g, ' ').trim())).toEqual([
        'Jueves 15/10/2026 · 2 recepciones · 2.000 L · sin desvíos',
        'Miércoles 14/10/2026 · 1 recepción · 900 L · sin desvíos',
      ]);
      expect(elemento().querySelector('.cargar-mas')).toBeNull();
      expect(texto('.pie')).toContain('3 de 12 recepciones');
    });

    it('never shows a row twice when a recepción registered meanwhile shifts the pages', async () => {
      await abrir('/');
      await cargar(listado({ items: [recepcion({ id: 'a' }), recepcion({ id: 'b', ticket: 4979 })], hayMas: true }));

      (elemento().querySelector('.cargar-mas button') as HTMLButtonElement).click();
      http
        .expectOne((r) => esListado(r) && r.params.get('pagina') === '2')
        .flush(listado({ items: [recepcion({ id: 'b', ticket: 4979 }), recepcion({ id: 'c', ticket: 4978 })], pagina: 2 }));
      harness.detectChanges();

      const tickets = Array.from(elemento().querySelectorAll('tr.mat-mdc-row:not(.fila-dia) td:nth-child(2)')).map((td) => td.textContent?.trim());
      expect(tickets).toEqual(['4980', '4979', '4978']);
    });

    it('keeps what is shown and offers to retry when the next page fails', async () => {
      await abrir('/');
      await cargar(listado({ hayMas: true }));

      (elemento().querySelector('.cargar-mas button') as HTMLButtonElement).click();
      http.expectOne((r) => esListado(r) && r.params.get('pagina') === '2').flush({}, { status: 500, statusText: 'Server Error' });
      harness.detectChanges();

      expect(texto('.error-mas')).toBe('No se pudieron cargar más recepciones.');
      expect(elemento().querySelectorAll('tr.mat-mdc-row:not(.fila-dia)')).toHaveLength(1);
      expect((elemento().querySelector('.cargar-mas button') as HTMLButtonElement).disabled).toBe(false);
    });
  });

  describe('recargar', () => {
    it('loads the first page again with the same filters, without the spinner', async () => {
      await abrir('/?desde=2026-10-14&hasta=2026-10-15&ticket=4980');
      await cargar();
      const panel = harness.fixture.debugElement.query(By.directive(RecepcionesPanel)).componentInstance as RecepcionesPanel;

      panel.recargar();
      harness.detectChanges();

      expect(elemento().querySelector('mat-spinner')).toBeNull();
      const req = pedidoListado();
      expect(req.request.params.get('ticket')).toBe('4980');
      expect(req.request.params.has('pagina')).toBe(false);
      req.flush(listado({ totales: totales({ cantidadRecepciones: 13 }) }));
      harness.detectChanges();
      expect(texto('.pie')).toContain('1 de 13 recepciones');
    });
  });
});
