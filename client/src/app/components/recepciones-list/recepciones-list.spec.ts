import { LOCALE_ID } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeEsAr from '@angular/common/locales/es-AR';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { MatTooltip } from '@angular/material/tooltip';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { RecepcionListado, SubtotalDia } from '../../models/recepcion-listado';
import { RecepcionesList, etiquetaDia } from './recepciones-list';

registerLocaleData(localeEsAr);

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

const dia = (fecha: string, cambios: Partial<SubtotalDia> = {}): SubtotalDia => ({
  fecha,
  cantidadRecepciones: 1,
  litros: 1214,
  cantidadFueraDeRango: 0,
  ...cambios,
});

describe('RecepcionesList', () => {
  let fixture: ComponentFixture<RecepcionesList>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RecepcionesList],
      providers: [provideRouter([]), { provide: LOCALE_ID, useValue: 'es-AR' }],
    }).compileComponents();

    fixture = TestBed.createComponent(RecepcionesList);
  });

  async function mostrar(
    items: RecepcionListado[],
    dias: SubtotalDia[] = [],
    entradas: Record<string, unknown> = {},
  ): Promise<void> {
    fixture.componentRef.setInput('items', items);
    fixture.componentRef.setInput('dias', dias);
    for (const [nombre, valor] of Object.entries(entradas)) {
      fixture.componentRef.setInput(nombre, valor);
    }
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const filas = (): HTMLElement[] =>
    Array.from(fixture.nativeElement.querySelectorAll('tr.mat-mdc-row:not(.fila-dia)'));
  const filasDia = (): HTMLElement[] => Array.from(fixture.nativeElement.querySelectorAll('tr.fila-dia'));
  const celdas = (fila: HTMLElement): string[] =>
    Array.from(fila.querySelectorAll('td')).map((td) => (td.textContent ?? '').trim());
  const texto = (el: Element): string => (el.textContent ?? '').replace(/\s+/g, ' ').trim();

  it('shows short one-line headers, with the full name in the tooltip and aria-label', async () => {
    await mostrar([recepcion({})]);

    const encabezados = fixture.debugElement.queryAll(By.css('th'));
    const visibles = encabezados.map((th) => th.nativeElement.textContent.trim());
    const completos = encabezados.map((th) => th.nativeElement.getAttribute('aria-label'));
    const tooltips = encabezados.map((th) => th.injector.get(MatTooltip, null)?.message ?? null);

    expect(visibles).toEqual([
      'Hora', 'Ticket', 'Proveedor', 'Litros', 'Agua %', 'L. reales', 'Grasa %', 'Acidez °D', 'Temp. °C', 'Silo', 'Estado', 'Obs.',
    ]);
    expect(completos).toEqual([
      'Hora de la recepción', 'Ticket', 'Proveedor', 'Litros', '% agua', 'Litros reales', 'Grasa (%)', 'Acidez (°D)',
      'Temperatura (°C)', 'Silo', 'Estado', 'Observaciones',
    ]);
    // abbreviated headers carry the full name as a tooltip; headers that are already complete don't repeat it
    expect(tooltips).toEqual([
      'Hora de la recepción', null, null, null, '% agua', 'Litros reales', 'Grasa (%)', 'Acidez (°D)',
      'Temperatura (°C)', null, null, 'Observaciones',
    ]);
  });

  describe('day groups', () => {
    const items = [
      recepcion({ id: 'a', ticket: 4980, fechaHora: '2026-10-15T09:26:00' }),
      recepcion({ id: 'b', ticket: 4979, fechaHora: '2026-10-15T09:02:00' }),
      recepcion({ id: 'c', ticket: 4974, fechaHora: '2026-10-14T09:50:00' }),
    ];

    it('groups the recepciones by day, newest first, under a header with the day subtotals', async () => {
      await mostrar(items, [
        dia('2026-10-15', { cantidadRecepciones: 6, litros: 12813, cantidadFueraDeRango: 3 }),
        dia('2026-10-14', { cantidadRecepciones: 1, litros: 1876 }),
      ]);

      const encabezados = filasDia().map(texto);
      expect(encabezados).toEqual([
        'Jueves 15/10/2026 · 6 recepciones · 12.813 L · 3 fuera de rango',
        'Miércoles 14/10/2026 · 1 recepción · 1.876 L · sin desvíos',
      ]);
      const orden = Array.from(
        fixture.nativeElement.querySelectorAll('tr.mat-mdc-row') as NodeListOf<HTMLElement>,
      ).map((tr) => (tr.classList.contains('fila-dia') ? 'dia' : celdas(tr)[1]));
      expect(orden).toEqual(['dia', '4980', '4979', 'dia', '4974']);
    });

    it('spans the whole row with the day header', async () => {
      await mostrar(items, [dia('2026-10-15')]);

      const celda = filasDia()[0].querySelector('td') as HTMLElement;

      expect(celda.getAttribute('colspan')).toBe('12');
    });

    it('shows only the day when its subtotal is not known', async () => {
      await mostrar(items, []);

      expect(filasDia().map(texto)).toEqual(['Jueves 15/10/2026', 'Miércoles 14/10/2026']);
    });

    it('formats a day label with the weekday of the plant date, not of the browser zone', () => {
      expect(etiquetaDia('2026-10-18')).toBe('Domingo 18/10/2026');
      expect(etiquetaDia('2026-01-01')).toBe('Jueves 01/01/2026');
    });
  });

  describe('cells', () => {
    it('shows the time, the ticket, the proveedor as a link to its page and the values in es-AR format', async () => {
      await mostrar([
        recepcion({
          ticket: 4978, proveedorId: 'p9', proveedorNombre: 'Tambo San José', litros: 1905, aguaPorcentaje: 1.5,
          litrosReales: 1876.425, grasa: 3.5, acidez: 17, temperatura: 4.9, silo: 2,
        }),
      ]);

      const [hora, ticket, proveedor, litros, agua, reales, grasa, acidez, temperatura, silo] = celdas(filas()[0]);
      const enlace = filas()[0].querySelector('a.proveedor-link') as HTMLAnchorElement;

      expect([hora, ticket, proveedor, litros, agua, reales, grasa, acidez, temperatura, silo]).toEqual([
        '09:26', '4978', 'Tambo San José', '1.905', '1,5', '1.876,425', '3,50', '17', '4,9', '2',
      ]);
      expect(enlace.getAttribute('href')).toBe('/proveedores/p9');
      expect(fixture.debugElement.query(By.css('a.proveedor-link')).injector.get(MatTooltip).message).toBe(
        'Tambo San José',
      );
    });

    it('shows a dash when ticket, water, fat and silo are empty and 0 when the water is zero', async () => {
      await mostrar([
        recepcion({ id: 'a', ticket: null, aguaPorcentaje: null, grasa: null, silo: null }),
        recepcion({ id: 'b', aguaPorcentaje: 0 }),
      ]);

      const [vacia, cero] = filas().map(celdas);

      expect([vacia[1], vacia[4], vacia[6], vacia[9]]).toEqual(['—', '—', '—', '—']);
      expect(cero[4]).toBe('0');
    });

    it('hides the proveedor column where the proveedor is fixed', async () => {
      await mostrar([recepcion({})], [dia('2026-10-15')], { mostrarProveedor: false });

      const visibles = fixture.debugElement.queryAll(By.css('th')).map((th) => th.nativeElement.textContent.trim());

      expect(visibles).not.toContain('Proveedor');
      expect(fixture.nativeElement.querySelector('a.proveedor-link')).toBeNull();
      expect(filasDia()[0].querySelector('td')!.getAttribute('colspan')).toBe('11');
    });
  });

  describe('Estado', () => {
    it('shows OK when the recepción is within the limits', async () => {
      await mostrar([recepcion({ motivos: [] })]);

      const badge = filas()[0].querySelector('.estado') as HTMLElement;

      expect(badge.textContent).toBe('OK');
      expect(badge.classList.contains('estado-ok')).toBe(true);
    });

    it.each([
      ['Temperatura', 'Temp. alta'],
      ['Acidez', 'Acidez alta'],
      ['Agua', 'Agua'],
    ] as const)('shows a badge with text for %s', async (motivo, etiqueta) => {
      await mostrar([recepcion({ motivos: [motivo] })]);

      const badge = filas()[0].querySelector('.estado') as HTMLElement;

      expect(badge.textContent).toBe(etiqueta);
      expect(badge.classList.contains('estado-alerta')).toBe(true);
    });

    it('shows how many more reasons there are and lists them all in the tooltip', async () => {
      await mostrar([recepcion({ motivos: ['Acidez', 'Temperatura', 'Agua'] })]);

      const badge = fixture.debugElement.query(By.css('.estado'));

      expect(badge.nativeElement.textContent).toBe('Acidez alta +2');
      expect(badge.injector.get(MatTooltip).message).toBe('Acidez alta, Temp. alta, Agua');
    });

    it('highlights only the values that are out of range', async () => {
      await mostrar([
        recepcion({ acidez: 20, temperatura: 21.3, aguaPorcentaje: 1.5, motivos: ['Acidez', 'Temperatura'] }),
      ]);

      const [, , , , agua, , , acidez, temperatura] = Array.from(filas()[0].querySelectorAll('td'));

      expect(acidez.classList.contains('alerta')).toBe(true);
      expect(temperatura.classList.contains('alerta')).toBe(true);
      expect(agua.classList.contains('alerta')).toBe(false);
    });
  });

  describe('Cargar más', () => {
    const boton = (): HTMLButtonElement | null =>
      fixture.nativeElement.querySelector('.cargar-mas button');

    it('is not shown when there is no next page', async () => {
      await mostrar([recepcion({})], [], { hayMas: false });

      expect(boton()).toBeNull();
    });

    it('is shown when there is a next page and asks for it when clicked', async () => {
      await mostrar([recepcion({})], [], { hayMas: true });
      let pedidos = 0;
      fixture.componentInstance.cargarMas.subscribe(() => pedidos++);

      boton()!.click();

      expect(boton()!.textContent?.trim()).toBe('Cargar más');
      expect(pedidos).toBe(1);
    });

    it('is disabled while the next page is loading', async () => {
      await mostrar([recepcion({})], [], { hayMas: true, cargandoMas: true });

      expect(boton()!.disabled).toBe(true);
      expect(boton()!.textContent?.trim()).toBe('Cargando...');
    });
  });

  describe('table conventions', () => {
    it('wraps the table in a horizontally scrollable container', async () => {
      await mostrar([recepcion({})]);

      expect(fixture.nativeElement.querySelector('.table-wrapper table.mat-mdc-table')).toBeTruthy();
    });

    it('aligns quantities to the right and identifiers to the center, each header like its column', async () => {
      await mostrar([recepcion({ observaciones: 'Acidez alta' })]);

      const alineacion = (el: Element) => {
        const clase = el.classList.contains('cantidad')
          ? 'cantidad'
          : el.classList.contains('identificador')
            ? 'identificador'
            : 'texto';
        return clase + ':' + getComputedStyle(el).textAlign;
      };
      const normalizar = (el: Element) => alineacion(el).replace(/:(left|start)$/, ':');
      const encabezados = Array.from(fixture.nativeElement.querySelectorAll('th') as NodeListOf<HTMLElement>);
      const celdasFila = Array.from(filas()[0].querySelectorAll('td'));

      // Hora, Ticket, Proveedor, Litros, Agua %, L. reales, Grasa %, Acidez °D, Temp. °C, Silo, Estado, Obs.
      const esperado = [
        'texto:', 'identificador:center', 'texto:', 'cantidad:right', 'cantidad:right', 'cantidad:right',
        'cantidad:right', 'cantidad:right', 'cantidad:right', 'identificador:center', 'texto:', 'texto:',
      ];
      expect(encabezados.map(normalizar)).toEqual(esperado);
      expect(celdasFila.map(normalizar)).toEqual(esperado);
    });

    it('uses a fixed layout with percentage widths, so it does not depend on the loaded rows', async () => {
      await mostrar([recepcion({})]);

      const tabla = fixture.nativeElement.querySelector('table') as HTMLElement;
      const ancho = (columna: string) =>
        getComputedStyle(fixture.nativeElement.querySelector(`th.mat-column-${columna}`) as HTMLElement).width;

      expect(getComputedStyle(tabla).tableLayout).toBe('fixed');
      expect(getComputedStyle(tabla).minWidth).toBe('1100px');
      expect({
        hora: ancho('hora'), ticket: ancho('ticket'), proveedor: ancho('proveedor'), litros: ancho('litros'),
        agua: ancho('agua'), litrosReales: ancho('litrosReales'), grasa: ancho('grasa'), acidez: ancho('acidez'),
        temperatura: ancho('temperatura'), silo: ancho('silo'), estado: ancho('estado'),
      }).toEqual({
        hora: '6%', ticket: '8%', proveedor: '15%', litros: '8%', agua: '6.5%', litrosReales: '8%',
        grasa: '6.5%', acidez: '7.5%', temperatura: '7.5%', silo: '4.5%', estado: '11%',
      });
      // Observaciones has no explicit width: it takes the rest
      expect(ancho('observaciones')).toBe('auto');
    });

    it('keeps headers and cells on one line, vertically centered, with the same vertical padding', async () => {
      await mostrar([recepcion({})]);

      const encabezado = getComputedStyle(fixture.nativeElement.querySelector('th.mat-column-litros') as HTMLElement);
      const celda = getComputedStyle(fixture.nativeElement.querySelector('td.mat-column-litros') as HTMLElement);

      expect(encabezado.whiteSpace).toBe('nowrap');
      expect(celda.whiteSpace).toBe('nowrap');
      expect(encabezado.verticalAlign).toBe('middle');
      expect(celda.verticalAlign).toBe('middle');
      // jsdom does not cascade Material's own padding rules, so the 12px is checked through the variable both use
      expect(
        getComputedStyle(fixture.nativeElement.querySelector('table') as HTMLElement).getPropertyValue(
          '--tabla-padding-vertical',
        ),
      ).toBe('12px');
      expect(celda.paddingTop).toBe(encabezado.paddingTop);
      expect(celda.paddingBottom).toBe(encabezado.paddingBottom);
    });

    it('uses the same padding on the first and the last column, header included', async () => {
      await mostrar([recepcion({})]);

      const estilo = (selector: string) => getComputedStyle(fixture.nativeElement.querySelector(selector) as HTMLElement);

      for (const [primera, ultima] of [
        ['th.mat-column-hora', 'th.mat-column-observaciones'],
        ['td.mat-column-hora', 'td.mat-column-observaciones'],
      ]) {
        expect(estilo(primera).paddingLeft).toBe('16px');
        expect(estilo(ultima).paddingRight).toBe('16px');
      }
    });
  });

  describe('observaciones', () => {
    const larga = 'Entrega con demora por lluvia; el camión llegó tarde y el silo estaba ocupado.';

    it('shows the observation as a button with the full text as tooltip, and nothing when empty', async () => {
      await mostrar([recepcion({ id: 'a', observaciones: larga }), recepcion({ id: 'b', observaciones: null })]);

      const botones = fixture.debugElement.queryAll(By.css('td app-observacion-celda button'));
      const celdasObs = fixture.debugElement.queryAll(By.css('td.mat-column-observaciones'));

      expect(botones).toHaveLength(1);
      expect(botones[0].nativeElement.textContent).toContain(larga);
      expect(botones[0].injector.get(MatTooltip).message).toBe(larga);
      expect(celdasObs).toHaveLength(2);
      expect(celdasObs[1].nativeElement.querySelector('button')).toBeNull();
    });

    it('opens a dialog with the full text, ticket, proveedor and fecha y hora when the observation is clicked', async () => {
      await mostrar([recepcion({ ticket: 4901, proveedorNombre: 'Tambo Test', fechaHora: '2026-10-14T08:18:00', observaciones: larga })]);

      (fixture.nativeElement.querySelector('td app-observacion-celda button') as HTMLButtonElement).click();
      await fixture.whenStable();
      fixture.detectChanges();

      const dialogo = document.querySelector('mat-dialog-container') as HTMLElement;
      expect(dialogo).toBeTruthy();
      expect(dialogo.textContent).toContain(larga);
      expect(dialogo.textContent).toContain('4901');
      expect(dialogo.textContent).toContain('Tambo Test');
      expect(dialogo.textContent).toContain('14/10/2026 08:18');

      TestBed.inject(MatDialog).closeAll();
    });
  });
});
