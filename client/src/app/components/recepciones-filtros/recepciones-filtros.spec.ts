import { LOCALE_ID } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeEsAr from '@angular/common/locales/es-AR';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FiltroRecepciones } from '../../models/recepcion-listado';
import { hoyEnPlanta, rangoDeAtajo, sumarDias } from '../../utils/periodos';
import { RecepcionesFiltros } from './recepciones-filtros';

registerLocaleData(localeEsAr);

const base = (cambios: Partial<FiltroRecepciones> = {}): FiltroRecepciones => ({
  ...rangoDeAtajo('hoy', hoyEnPlanta()),
  proveedorId: null,
  ticket: null,
  soloFueraDeRango: false,
  ...cambios,
});

describe('RecepcionesFiltros', () => {
  let fixture: ComponentFixture<RecepcionesFiltros>;
  let componente: RecepcionesFiltros;
  let emitidos: FiltroRecepciones[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RecepcionesFiltros],
      providers: [{ provide: LOCALE_ID, useValue: 'es-AR' }],
    }).compileComponents();

    fixture = TestBed.createComponent(RecepcionesFiltros);
    componente = fixture.componentInstance;
    emitidos = [];
    componente.cambio.subscribe((f) => emitidos.push(f));
  });

  async function mostrar(filtros: FiltroRecepciones, proveedores: { id: string; nombre: string; cantidadRecepciones: number; activo?: boolean }[] | null = null): Promise<void> {
    fixture.componentRef.setInput('filtros', filtros);
    fixture.componentRef.setInput('proveedores', proveedores);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const el = (): HTMLElement => fixture.nativeElement;
  const interno = (): any => componente as any; // the template-only members are protected
  const botones = (): string[] =>
    Array.from(el().querySelectorAll('mat-button-toggle')).map((b) => (b.textContent ?? '').trim());
  const seleccionado = (): string | null =>
    el().querySelector('mat-button-toggle.mat-button-toggle-checked')?.textContent?.trim().toLowerCase() ?? null;

  it('offers Hoy, Ayer, Semana and Mes', async () => {
    await mostrar(base());

    expect(botones()).toEqual(['Hoy', 'Ayer', 'Semana', 'Mes']);
  });

  describe('quick period', () => {
    it.each(['hoy', 'ayer', 'semana', 'mes'] as const)('marks %s when the range is exactly that period', async (atajo) => {
      await mostrar(base(rangoDeAtajo(atajo, hoyEnPlanta())));

      // "semana" and "mes" can coincide with "hoy" on a Monday / the 1st: the first match wins
      expect(['hoy', atajo]).toContain(seleccionado());
    });

    it('marks none for a custom range', async () => {
      await mostrar(base({ desde: '2026-01-03', hasta: '2026-01-09' }));

      expect(seleccionado()).toBeNull();
    });

    it('emits the plant-time range of the chosen period, keeping the other filters', async () => {
      await mostrar(base({ proveedorId: 'p1', ticket: 4980, soloFueraDeRango: true }));

      interno().seleccionarAtajo('ayer');

      const ayer = sumarDias(hoyEnPlanta(), -1);
      expect(emitidos).toEqual([
        { desde: ayer, hasta: ayer, proveedorId: 'p1', ticket: 4980, soloFueraDeRango: true, pagina: undefined },
      ]);
    });

    it('ignores an empty selection', async () => {
      await mostrar(base());

      interno().seleccionarAtajo(undefined);

      expect(emitidos).toEqual([]);
    });
  });

  describe('custom range', () => {
    const elegir = (inicio: Date | null, fin: Date | null): void => {
      interno().rangoForm.setValue({ inicio, fin });
      interno().rangoPersonalizadoCambiado();
    };

    it('emits the range once both dates are chosen', async () => {
      await mostrar(base());

      elegir(new Date(2026, 9, 1), new Date(2026, 9, 15));

      expect(emitidos).toHaveLength(1);
      expect(emitidos[0].desde).toBe('2026-10-01');
      expect(emitidos[0].hasta).toBe('2026-10-15');
    });

    it('waits for the end date', async () => {
      await mostrar(base());

      elegir(new Date(2026, 9, 1), null);

      expect(emitidos).toEqual([]);
    });

    it('rejects a start before 01/01/2000 with a message and does not emit', async () => {
      await mostrar(base());

      elegir(new Date(1999, 11, 31), new Date(2000, 0, 5));
      fixture.detectChanges();

      expect(emitidos).toEqual([]);
      expect(el().querySelector('.error-rango')?.textContent).toContain('01/01/2000');
    });

    it('rejects a range longer than 366 days with a message and does not emit', async () => {
      await mostrar(base());

      elegir(new Date(2025, 0, 1), new Date(2026, 9, 1));
      fixture.detectChanges();

      expect(emitidos).toEqual([]);
      expect(el().querySelector('.error-rango')?.textContent).toContain('366');
    });

    it('clears the message when a valid range is chosen afterwards', async () => {
      await mostrar(base());
      elegir(new Date(1999, 11, 31), new Date(2000, 0, 5));

      elegir(new Date(2026, 9, 1), new Date(2026, 9, 2));
      fixture.detectChanges();

      expect(el().querySelector('.error-rango')).toBeNull();
    });
  });

  describe('proveedor', () => {
    const lista = [
      { id: 'p1', nombre: 'Tambo Don Pedro', cantidadRecepciones: 1 },
      { id: 'p2', nombre: 'Tambo San José', cantidadRecepciones: 2 },
    ];

    it('is not offered when the proveedor is fixed by the page', async () => {
      await mostrar(base(), null);

      expect(el().querySelector('.campo-proveedor')).toBeNull();
    });

    it('is offered with "Todos" selected by default', async () => {
      await mostrar(base(), lista);

      expect(el().querySelector('.campo-proveedor')).toBeTruthy();
      expect(interno().proveedorControl.value).toBe('todos');
    });

    it('follows the filters (the URL) when they change from outside', async () => {
      await mostrar(base({ proveedorId: 'p2' }), lista);

      expect(interno().proveedorControl.value).toBe('p2');
    });

    it('emits the chosen proveedor, and null for "Todos"', async () => {
      await mostrar(base(), lista);

      interno().proveedorControl.setValue('p1');
      interno().proveedorCambiado();
      interno().proveedorControl.setValue('todos');
      interno().proveedorCambiado();

      expect(emitidos.map((f) => f.proveedorId)).toEqual(['p1', null]);
    });
  });

  describe('ticket', () => {
    beforeEach(() => vi.useFakeTimers());
    afterEach(() => vi.useRealTimers());

    it('shows the ticket of the filters', async () => {
      await mostrar(base({ ticket: 4980 }));

      expect(interno().ticketControl.value).toBe('4980');
    });

    it('emits the ticket once the operator stops typing, not on every key', async () => {
      await mostrar(base());

      interno().ticketControl.setValue('4');
      interno().ticketControl.setValue('49');
      interno().ticketControl.setValue('4980');
      expect(emitidos).toEqual([]);
      vi.advanceTimersByTime(400);

      expect(emitidos).toHaveLength(1);
      expect(emitidos[0].ticket).toBe(4980);
    });

    it('reads the ticket the es-AR way: "1.234" is ticket 1234', async () => {
      await mostrar(base());

      interno().ticketControl.setValue('1.234');
      vi.advanceTimersByTime(400);

      expect(emitidos).toHaveLength(1);
      expect(emitidos[0].ticket).toBe(1234);
    });

    it('rejects a decimal ticket with the integer message and does not emit', async () => {
      await mostrar(base());

      const campo = el().querySelector('.campo-ticket input') as HTMLInputElement;
      campo.value = '1,5';
      campo.dispatchEvent(new Event('input'));
      vi.advanceTimersByTime(400);
      fixture.detectChanges();

      expect(emitidos).toEqual([]);
      expect(fixture.nativeElement.textContent).toContain('Ingresá un número entero, sin decimales.');
    });

    it('emits null when the ticket is cleared', async () => {
      await mostrar(base({ ticket: 4980 }));

      interno().ticketControl.setValue('');
      vi.advanceTimersByTime(400);

      expect(emitidos[0].ticket).toBeNull();
    });

    it.each([['abc'], ['-5'], ['1.5'], ['0'], ['2147483648'], ['4980; DROP TABLE'], ['<script>x</script>']])(
      'does not emit the invalid ticket "%s" and shows an error',
      async (valor) => {
        await mostrar(base());

        interno().ticketControl.setValue(valor);
        vi.advanceTimersByTime(400);
        interno().ticketControl.markAsTouched();
        fixture.detectChanges();

        expect(emitidos).toEqual([]);
        expect(interno().ticketControl.invalid).toBe(true);
      },
    );

    it('keeps a ticket that is still being typed when another filter changes right after', async () => {
      await mostrar(base());

      interno().ticketControl.setValue('4980'); // the debounce has not fired yet
      interno().soloControl.setValue(true);
      interno().soloCambiado();

      expect(emitidos).toHaveLength(1);
      expect(emitidos[0].ticket).toBe(4980);
      expect(emitidos[0].soloFueraDeRango).toBe(true);
    });

    it('keeps the ticket of the URL when what is typed is invalid and another filter changes', async () => {
      await mostrar(base({ ticket: 4980 }));

      interno().ticketControl.setValue('abc');
      interno().soloControl.setValue(true);
      interno().soloCambiado();

      expect(emitidos[0].ticket).toBe(4980);
    });

    it('shows the error while typing, without waiting for the field to lose the focus', async () => {
      await mostrar(base());
      const campo = el().querySelector('.campo-ticket input') as HTMLInputElement;

      campo.value = '0';
      campo.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      expect(el().querySelector('.campo-ticket mat-error')?.textContent).toContain('Número entero entre 1 y 2.147.483.647.');
    });

    it('shows the parser message while typing text it cannot read', async () => {
      await mostrar(base());
      const campo = el().querySelector('.campo-ticket input') as HTMLInputElement;

      campo.value = '-5';
      campo.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      expect(el().querySelector('.campo-ticket mat-error')?.textContent).toContain('Escribí solo números enteros');
    });

    it('shows no error for a valid or empty ticket', async () => {
      await mostrar(base());
      const campo = el().querySelector('.campo-ticket input') as HTMLInputElement;

      campo.value = '4980';
      campo.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      expect(el().querySelector('.campo-ticket mat-error')).toBeNull();
    });

    it.each([['   '], ['	'], [' 4980 ']])('treats the whitespace in %j as an empty search or trims it', async (valor) => {
      await mostrar(base());

      interno().ticketControl.setValue(valor);
      vi.advanceTimersByTime(400);

      expect(interno().ticketControl.valid).toBe(true);
      expect(emitidos[0].ticket).toBe(valor.trim() === '' ? null : 4980);
    });

    it('does not overwrite what is typed when the URL echoes the ticket it asked for', async () => {
      await mostrar(base());

      interno().ticketControl.setValue('5');
      vi.advanceTimersByTime(400); // emits 5
      interno().ticketControl.setValue('56'); // typed before the URL answered
      await mostrar(base({ ticket: 5 })); // the URL arrives with the old value

      expect(interno().ticketControl.value).toBe('56');
    });

    it('does follow a ticket that changes from outside (e.g. back navigation)', async () => {
      await mostrar(base({ ticket: 5 }));
      interno().ticketControl.setValue('56');
      vi.advanceTimersByTime(400);

      await mostrar(base({ ticket: 9 }));

      expect(interno().ticketControl.value).toBe('9');
    });

    it('accepts the largest int', async () => {
      await mostrar(base());

      interno().ticketControl.setValue('2147483647');
      vi.advanceTimersByTime(400);

      expect(emitidos[0].ticket).toBe(2147483647);
    });
  });

  describe('Solo fuera de rango', () => {
    it('reflects the filter and emits when toggled', async () => {
      await mostrar(base({ soloFueraDeRango: true }));
      expect(interno().soloControl.value).toBe(true);

      interno().soloControl.setValue(false);
      interno().soloCambiado();

      expect(emitidos[0].soloFueraDeRango).toBe(false);
    });
  });

  describe('Incluir anuladas', () => {
    it('is off by default and emits the filter when toggled', async () => {
      await mostrar(base());
      expect(interno().anuladasControl.value).toBe(false);

      interno().anuladasControl.setValue(true);
      interno().anuladasCambiado();

      expect(emitidos[0].incluirAnuladas).toBe(true);
      expect(emitidos[0].soloFueraDeRango).toBe(false); // the other filters are kept
    });

    it('follows the filter that comes from the URL', async () => {
      await mostrar(base({ incluirAnuladas: true }));

      expect(interno().anuladasControl.value).toBe(true);
      expect((el().textContent ?? '')).toContain('Incluir anuladas');
    });

    it('marks an inactive proveedor in the selector, so its history can still be filtered', async () => {
      await mostrar(base(), [
        { id: 'p1', nombre: 'Tambo Activo', cantidadRecepciones: 1, activo: true },
        { id: 'p2', nombre: 'Tambo Cerrado', cantidadRecepciones: 4, activo: false },
      ]);
      (el().querySelector('mat-select') as HTMLElement).click();
      await fixture.whenStable();
      fixture.detectChanges();

      const opciones = Array.from(document.querySelectorAll('mat-option')).map((o) => (o.textContent ?? '').trim());
      expect(opciones).toEqual(['Todos', 'Tambo Activo', 'Tambo Cerrado (inactivo)']);
    });
  });

  it('never resets the page when emitting a change (the list goes back to the first page)', async () => {
    await mostrar({ ...base(), pagina: 3 });

    interno().soloControl.setValue(true);
    interno().soloCambiado();

    expect(emitidos[0].pagina).toBeUndefined();
  });
});
