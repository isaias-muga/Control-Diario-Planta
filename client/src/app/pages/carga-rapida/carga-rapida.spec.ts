import { LOCALE_ID } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeEsAr from '@angular/common/locales/es-AR';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { CargaRapida } from './carga-rapida';

registerLocaleData(localeEsAr);

const LIMITES = { acidezMaxima: 19, temperaturaMaxima: 20, aguaMaxima: 0, actualizadoEn: '2026-10-15T00:00:00Z' };
const PROVEEDOR = { id: 'p1', nombre: 'Tambo Don Pedro', cantidadRecepciones: 3 };

describe('CargaRapida', () => {
  let fixture: ComponentFixture<CargaRapida>;
  let component: CargaRapida;
  let http: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: LOCALE_ID, useValue: 'es-AR' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined);
    fixture = TestBed.createComponent(CargaRapida);
    component = fixture.componentInstance;
    await fixture.whenStable();
    http.expectOne((r) => r.url.endsWith('/Proveedores')).flush([PROVEEDOR]);
    http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush(LIMITES);
    http.expectOne((r) => r.url.endsWith('/Recepciones/siguiente-ticket')).flush({ ticket: 4981 });
    await fixture.whenStable();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    http.verify();
  });

  const el = (): HTMLElement => fixture.nativeElement;
  const texto = (): string => (el().textContent ?? '').replace(/\s+/g, ' ').trim();

  /** The value shown next to a label (dt/dd pair) on the confirmation or the done step. */
  function dato(etiqueta: string): string | undefined {
    const dt = Array.from(el().querySelectorAll('dt')).find((d) => d.textContent?.trim() === etiqueta);
    return dt?.nextElementSibling?.textContent?.replace(/\s+/g, ' ').trim();
  }

  function cargar(valores: Record<string, unknown> = {}): void {
    component.form.patchValue({
      proveedorId: 'p1',
      ticket: 4981,
      litros: 1000,
      acidez: 17,
      temperatura: 5,
      ...valores,
    });
  }

  function enviar(): void {
    component.enviar();
    fixture.detectChanges();
  }

  const peticionPost = () => http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/Proveedores/p1/recepciones'));

  it('starts on the load step with the suggested ticket', () => {
    expect(component.paso()).toBe('carga');
    expect(component.form.controls.ticket.value).toBe(4981);
  });

  it('requires the ticket (the dialog keeps it optional)', () => {
    cargar({ ticket: null });

    component.continuar();

    expect(component.paso()).toBe('carga');
    expect(component.form.controls.ticket.hasError('required')).toBe(true);
  });

  it('requires the ticket even when it was never touched (no suggestion arrived)', () => {
    cargar({ ticket: null });
    component.form.controls.ticket.markAsPristine();

    component.continuar();

    expect(component.paso()).toBe('carga');
  });

  it('does not leave the load step while the form is invalid', () => {
    cargar({ acidez: 5 });

    component.continuar();

    expect(component.paso()).toBe('carga');
  });

  it('goes to the confirmation and shows the values and the fecha/hora that will be saved', () => {
    cargar({ aguaPorcentaje: 10 });

    component.continuar();
    fixture.detectChanges();

    expect(component.paso()).toBe('confirmar');
    expect(texto()).toContain('¿Son correctos estos datos?');
    expect(dato('Tambo')).toBe('Tambo Don Pedro');
    expect(dato('Litros a pagar')).toBe('900');
    expect(dato('Fecha y hora')).toMatch(/^\d{2}\/\d{2}\/\d{4} \d{2}:\d{2}$/);
  });

  it('keeps the values when the operator goes back with Corregir', () => {
    cargar({ observaciones: 'Leche tibia' });
    component.continuar();

    component.corregir();
    fixture.detectChanges();

    expect(component.paso()).toBe('carga');
    expect(component.form.getRawValue()).toMatchObject({ proveedorId: 'p1', ticket: 4981, litros: 1000, observaciones: 'Leche tibia' });
  });

  it('highlights the readings over the quality limits with the limit, and only those', () => {
    cargar({ acidez: 25 });

    component.continuar();
    fixture.detectChanges();

    const fuera = Array.from(el().querySelectorAll('.fila.fuera')).map((d) => (d.textContent ?? '').replace(/\s+/g, ' ').trim());
    expect(fuera).toHaveLength(1);
    expect(fuera[0]).toContain('Acidez 25 °D');
    expect(fuera[0]).toContain('Fuera de rango (máx. 19)');
  });

  it('shows "Litros a pagar" only when there is water, and a dash for empty optional values', () => {
    cargar();
    component.continuar();
    fixture.detectChanges();

    expect(dato('Litros a pagar')).toBeUndefined();
    expect(dato('Grasa')).toBe('—');
    expect(dato('Agua')).toBe('—');
    expect(dato('Silo')).toBe('—');
  });

  it('highlights nothing when every reading is within the limits', () => {
    cargar();

    component.continuar();
    fixture.detectChanges();

    expect(el().querySelectorAll('.fila.fuera')).toHaveLength(0);
  });

  it('sends the recepción and shows the entered ticket, proveedor and litros on the done step', () => {
    cargar({ litros: 1200, aguaPorcentaje: 10 });
    component.continuar();
    const fechaHora = component.form.controls.fechaHora.value;

    enviar();
    const req = peticionPost();
    expect(req.request.body).toMatchObject({ fechaHora, litros: 1200, aguaPorcentaje: 10, ticket: 4981, acidez: 17, temperatura: 5 });
    req.flush(PROVEEDOR);
    fixture.detectChanges();

    expect(component.paso()).toBe('listo');
    expect(texto()).toContain('Recepción guardada');
    expect(texto()).toContain('Ticket 4981 · 1.200 L');
    expect(texto()).toContain('Tambo Don Pedro');
    expect(texto()).toContain('¿Te equivocaste? Avisá en la oficina: la corrección la hace el administrador.');
    expect(texto()).not.toContain('1.080');
  });

  it('sends one request when Confirmar is tapped twice', () => {
    cargar();
    component.continuar();

    component.enviar();
    component.enviar();

    peticionPost().flush(PROVEEDOR);
  });

  it('starts an empty form (with the next suggested ticket) on Nueva recepción', () => {
    cargar();
    component.continuar();
    enviar();
    peticionPost().flush(PROVEEDOR);

    component.nuevaRecepcion();
    http.expectOne((r) => r.url.endsWith('/Recepciones/siguiente-ticket')).flush({ ticket: 4982 });
    fixture.detectChanges();

    expect(component.paso()).toBe('carga');
    expect(component.form.getRawValue()).toMatchObject({ proveedorId: '', litros: null, acidez: null, temperatura: null, ticket: 4982 });
  });

  describe('errors', () => {
    it('answers a duplicate ticket with the office message and returns to the load step with the values kept', () => {
      cargar();
      component.continuar();

      enviar();
      peticionPost().flush({ error: 'El ticket 4981 ya fue usado.' }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(component.paso()).toBe('carga');
      expect(texto()).toContain('Ese ticket ya fue cargado. Consultá en la oficina.');
      expect(component.form.getRawValue()).toMatchObject({ proveedorId: 'p1', ticket: 4981, litros: 1000, acidez: 17 });
    });

    it('returns to the load step with the message when the API rejects a value (400)', () => {
      cargar();
      component.continuar();

      enviar();
      peticionPost().flush({ error: 'Los litros son demasiado altos.', field: 'litros' }, { status: 400, statusText: 'Bad Request' });
      fixture.detectChanges();

      expect(component.paso()).toBe('carga');
      expect(texto()).toContain('Los litros son demasiado altos.');
    });

    it('shows "No se guardó" with a retry on a network error, staying on the confirmation', () => {
      cargar();
      component.continuar();

      enviar();
      peticionPost().error(new ProgressEvent('error'));
      fixture.detectChanges();

      expect(component.paso()).toBe('confirmar');
      expect(texto()).toContain('No se guardó');
      expect(texto()).toContain('No se pudo conectar con el servidor.');
      expect(el().textContent).toContain('Reintentar');
    });

    it('resends the same data (same fecha/hora) on Reintentar and finishes when it works', () => {
      cargar();
      component.continuar();
      enviar();
      const primero = peticionPost();
      const cuerpo = primero.request.body;
      primero.error(new ProgressEvent('error'));

      enviar();
      const segundo = peticionPost();
      segundo.flush(PROVEEDOR);

      expect(segundo.request.body).toEqual(cuerpo);
      expect(component.paso()).toBe('listo');
    });

    it('says it was probably saved when a retry answers 409, with no retry button', () => {
      cargar();
      component.continuar();
      enviar();
      peticionPost().error(new ProgressEvent('error'));

      enviar();
      peticionPost().flush({ error: 'El ticket 4981 ya fue usado.' }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(component.paso()).toBe('confirmar');
      expect(texto()).toContain('Es probable que ya se haya guardado. Consultá en la oficina antes de cargarla de nuevo.');
      expect(texto()).not.toContain('Ese ticket ya fue cargado');
      expect(Array.from(el().querySelectorAll('button')).map((b) => b.textContent?.trim())).not.toContain('Reintentar');
    });

    it('treats a 409 after a retry that was rejected with 400 as a plain duplicate', () => {
      cargar();
      component.continuar();
      enviar();
      peticionPost().error(new ProgressEvent('error'));
      enviar();
      peticionPost().flush({ error: 'Los litros son demasiado altos.' }, { status: 400, statusText: 'Bad Request' });
      component.continuar();

      enviar();
      peticionPost().flush({ error: 'El ticket 4981 ya fue usado.' }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(texto()).toContain('Ese ticket ya fue cargado. Consultá en la oficina.');
    });

    it('treats a 409 after Corregir as a plain duplicate again', () => {
      cargar();
      component.continuar();
      enviar();
      peticionPost().error(new ProgressEvent('error'));
      component.corregir();
      component.continuar();

      enviar();
      peticionPost().flush({ error: 'El ticket 4981 ya fue usado.' }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(component.paso()).toBe('carga');
      expect(texto()).toContain('Ese ticket ya fue cargado. Consultá en la oficina.');
    });
  });
});
