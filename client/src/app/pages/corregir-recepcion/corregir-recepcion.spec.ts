import { LOCALE_ID } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeEsAr from '@angular/common/locales/es-AR';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RecepcionDetalle } from '../../models/correcciones';
import { Proveedor } from '../../models/proveedor';
import { cerrarSesionDePrueba, iniciarSesionDePrueba } from '../../testing/sesion';
import { CorregirRecepcion } from './corregir-recepcion';

registerLocaleData(localeEsAr);

const DETALLE: RecepcionDetalle = {
  id: 'r1',
  proveedorId: 'p1',
  proveedorNombre: 'Tambo Don Pedro',
  proveedorActivo: true,
  ticket: 4980,
  fechaHora: '2026-10-01T06:00:00',
  litros: 1000,
  aguaPorcentaje: 1,
  grasa: 3.5,
  acidez: 18,
  temperatura: 4,
  silo: 1,
  observaciones: 'Primera carga',
  anulada: false,
  version: 'AAAAAAAAAAE=',
};

const PROVEEDORES: Proveedor[] = [
  { id: 'p1', nombre: 'Tambo Don Pedro', cantidadRecepciones: 3, activo: true },
  { id: 'p2', nombre: 'Tambo San José', cantidadRecepciones: 1, activo: true },
  { id: 'p3', nombre: 'Tambo Cerrado', cantidadRecepciones: 9, activo: false },
];

const LIMITES = { acidezMaxima: 19, temperaturaMaxima: 20, aguaMaxima: 0, cantidadSilos: 3, actualizadoEn: '2026-10-15T00:00:00Z', version: 'v' };

describe('CorregirRecepcion', () => {
  let fixture: ComponentFixture<CorregirRecepcion>;
  let component: CorregirRecepcion;
  let http: HttpTestingController;

  beforeEach(() => {
    iniciarSesionDePrueba('Admin');
    TestBed.configureTestingModule({
      imports: [CorregirRecepcion],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), { provide: LOCALE_ID, useValue: 'es-AR' }],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CorregirRecepcion);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('id', 'r1');
  });

  afterEach(() => {
    http.verify();
    cerrarSesionDePrueba();
  });

  const interno = (): any => component as any; // the template-only members are protected
  const el = (): HTMLElement => fixture.nativeElement;
  const texto = (): string => (el().textContent ?? '').replace(/\s+/g, ' ').trim();
  const boton = (nombre: string): HTMLButtonElement =>
    Array.from(el().querySelectorAll('button')).find((b) => (b.textContent ?? '').trim() === nombre) as HTMLButtonElement;

  /** The page asks for the recepción, the proveedores and the number of silos when it opens. */
  async function abrir(detalle: RecepcionDetalle = DETALLE, proveedores: Proveedor[] = PROVEEDORES): Promise<void> {
    await fixture.whenStable();
    http.expectOne((r) => r.url.endsWith('/Recepciones/r1')).flush(detalle);
    http.expectOne((r) => r.url.endsWith('/Proveedores')).flush(proveedores);
    http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush(LIMITES);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function cambiar(valores: Record<string, unknown>, motivo: string | null = 'Error de tipeo en la balanza'): void {
    interno().form.patchValue(valores);
    if (motivo !== null) {
      interno().motivo.setValue(motivo);
    }
  }

  async function confirmar(valores: Record<string, unknown> = { litros: 1250.5 }): Promise<void> {
    cambiar(valores);
    interno().continuar();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const pedidoPut = (): TestRequest => http.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/Recepciones/r1'));

  describe('loading', () => {
    it('shows a loading message until everything arrived, and no controls before', async () => {
      await fixture.whenStable();
      fixture.detectChanges();

      expect(texto()).toContain('Cargando la recepción…');
      expect(el().querySelector('form')).toBeNull();

      http.expectOne((r) => r.url.endsWith('/Recepciones/r1')).flush(DETALLE);
      http.expectOne((r) => r.url.endsWith('/Proveedores')).flush(PROVEEDORES);
      http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush(LIMITES);
    });

    it('fills the form with the values of the recepción', async () => {
      await abrir();

      const v = interno().form.getRawValue();
      expect(el().querySelector('h2')?.textContent).toBe('Corregir recepción');
      expect(v).toMatchObject({ proveedorId: 'p1', litros: 1000, grasa: 3.5, acidez: 18, temperatura: 4, aguaPorcentaje: 1, silo: 1, ticket: 4980, observaciones: 'Primera carga' });
      expect(v.fechaHora).toBe('2026-10-01T06:00');
      expect(texto()).toContain('Ticket 4980 · Tambo Don Pedro');
    });

    it('says the recepción was not found when the API answers 404 or the id is not valid', async () => {
      await fixture.whenStable();
      // The first error cancels the rest of the requests, so the others are answered first
      http.expectOne((r) => r.url.endsWith('/Proveedores')).flush(PROVEEDORES);
      http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush(LIMITES);
      http.expectOne((r) => r.url.endsWith('/Recepciones/r1')).flush({}, { status: 404, statusText: 'Not Found' });
      await fixture.whenStable();
      fixture.detectChanges();

      expect(texto()).toContain('Recepción no encontrada.');
    });

    it('shows the message of the API when it cannot load', async () => {
      await fixture.whenStable();
      http.expectOne((r) => r.url.endsWith('/Proveedores')).flush(PROVEEDORES);
      http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush(LIMITES);
      http.expectOne((r) => r.url.endsWith('/Recepciones/r1')).flush({ error: 'x' }, { status: 500, statusText: 'Server Error' });
      await fixture.whenStable();
      fixture.detectChanges();

      expect(el().querySelector('.form-error')?.textContent).toContain('No se pudo cargar la recepción.');
    });
  });

  describe('what can be chosen', () => {
    it('offers the active proveedores only, in alphabetical order', async () => {
      await abrir();

      expect(interno().proveedoresOfrecidos().map((p: Proveedor) => p.nombre)).toEqual(['Tambo Don Pedro', 'Tambo San José']);
    });

    it('also offers the recepción\'s own proveedor when it was deactivated since, so it can keep it', async () => {
      await abrir({ ...DETALLE, proveedorId: 'p3', proveedorNombre: 'Tambo Cerrado', proveedorActivo: false });

      expect(interno().proveedoresOfrecidos().map((p: Proveedor) => p.nombre)).toEqual(['Tambo Cerrado', 'Tambo Don Pedro', 'Tambo San José']);
    });

    it('offers the silos 1..N of the plant', async () => {
      await abrir();

      expect(interno().silos()).toEqual([1, 2, 3]);
      expect(el().querySelectorAll('.silo-boton')).toHaveLength(3);
      expect(el().querySelector('.silo-boton.elegido')?.textContent?.trim()).toBe('1');
    });

    it('also offers the silo the recepción has when the plant has fewer silos now', async () => {
      await abrir({ ...DETALLE, silo: 5 });

      expect(interno().silos()).toEqual([1, 2, 3, 4, 5]);
    });

    it('chooses a silo with the buttons', async () => {
      await abrir();

      (el().querySelectorAll('.silo-boton')[1] as HTMLButtonElement).click();

      expect(interno().form.controls.silo.value).toBe(2);
    });
  });

  describe('the ticket', () => {
    it('is required when the recepción has one: it cannot be removed', async () => {
      await abrir();

      interno().form.controls.ticket.setValue(null);

      expect(interno().form.controls.ticket.errors).toHaveProperty('required');
    });

    it('stays optional when the recepción has none', async () => {
      await abrir({ ...DETALLE, ticket: null });

      interno().form.controls.ticket.setValue(null);

      expect(interno().form.controls.ticket.valid).toBe(true);
      expect(texto()).toContain('Ticket (opcional)');
    });
  });

  describe('step 1: edit', () => {
    it('does not go on when nothing changed, even with a reason', async () => {
      await abrir();
      interno().motivo.setValue('Error de tipeo en la balanza');

      interno().continuar();
      fixture.detectChanges();

      expect(interno().paso()).toBe('editar');
      expect(texto()).toContain('No cambiaste ningún dato.');
    });

    it.each([null, '', '   ', 'abcd'])('does not go on without a reason of at least 5 characters (%j)', async (motivo) => {
      await abrir();
      cambiar({ litros: 1250.5 }, motivo ?? '');

      interno().continuar();
      fixture.detectChanges();

      expect(interno().paso()).toBe('editar');
      expect(texto()).toContain('Escribí el motivo (mínimo 5 caracteres)');
    });

    it.each([
      ['litros', 0],
      ['litros', 30001],
      ['acidez', 5],
      ['temperatura', 50],
      ['aguaPorcentaje', 101],
      ['grasa', 11],
      ['silo', 21],
    ])('does not go on when %s is %s (a plausible range of the domain)', async (campo, valor) => {
      await abrir();
      cambiar({ [campo]: valor });

      interno().continuar();

      expect(interno().paso()).toBe('editar');
    });

    it('does not go on with a date in the future', async () => {
      await abrir();
      cambiar({ fechaHora: '2999-01-01T00:00' });

      interno().continuar();

      expect(interno().paso()).toBe('editar');
    });

    it('does not offer to correct a voided recepción', async () => {
      await abrir({ ...DETALLE, anulada: true });

      expect(texto()).toContain('Esta recepción está anulada: no se puede corregir.');
      expect(boton('Revisar cambios').disabled).toBe(true);
    });
  });

  describe('step 2: confirm', () => {
    it('lists old to new for every changed field and the reason', async () => {
      await abrir();

      await confirmar({ litros: 1250.5, silo: 2, observaciones: '' });

      expect(interno().paso()).toBe('confirmar');
      expect(texto()).toContain('Litros1.000 → 1.250,5');
      expect(texto()).toContain('Silo1 → 2');
      expect(texto()).toContain('ObservacionesPrimera carga → —');
      expect(texto()).toContain('MotivoError de tipeo en la balanza');
      expect(texto()).not.toContain('Acidez'); // what did not change is not listed
    });

    it('shows the proveedor by name', async () => {
      await abrir();

      await confirmar({ proveedorId: 'p2' });

      expect(texto()).toContain('ProveedorTambo Don Pedro → Tambo San José');
    });

    it('goes back to the form with what was typed when Corregir is pressed', async () => {
      await abrir();
      await confirmar();

      boton('Corregir').click();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(interno().paso()).toBe('editar');
      expect(interno().form.controls.litros.value).toBe(1250.5);
      expect(interno().motivo.value).toBe('Error de tipeo en la balanza');
    });

    it('sends the new values, the trimmed reason and the version that was read', async () => {
      await abrir();
      await confirmar({ litros: 1250.5 });
      interno().motivo.setValue('  Error de tipeo en la balanza  ');

      interno().enviar();

      const req = pedidoPut();
      expect(req.request.body).toEqual({
        proveedorId: 'p1', litros: 1250.5, grasa: 3.5, acidez: 18, temperatura: 4, aguaPorcentaje: 1, silo: 1, ticket: 4980,
        fechaHora: '2026-10-01T06:00:00', observaciones: 'Primera carga', motivo: 'Error de tipeo en la balanza', version: 'AAAAAAAAAAE=',
      });
      req.flush({ ...DETALLE, litros: 1250.5 });
    });

    it('keeps the seconds of the date when the user did not change it, and sends the new date when they did', async () => {
      await abrir({ ...DETALLE, fechaHora: '2026-10-01T06:00:45' });
      await confirmar({ litros: 1250 });
      interno().enviar();
      const sinTocar = pedidoPut();
      expect(sinTocar.request.body.fechaHora).toBe('2026-10-01T06:00:45');
      sinTocar.flush(DETALLE);
      await fixture.whenStable();

      interno().volverAEditar();
      cambiar({ fechaHora: '2026-10-02T07:30' });
      interno().continuar();
      interno().enviar();
      const tocada = pedidoPut();
      expect(tocada.request.body.fechaHora).toBe('2026-10-02T07:30');
      tocada.flush(DETALLE);
    });

    it('sends blank observaciones as null', async () => {
      await abrir();
      await confirmar({ observaciones: '   ' });

      interno().enviar();

      const req = pedidoPut();
      expect(req.request.body.observaciones).toBeNull();
      req.flush(DETALLE);
    });

    it('sends only one request when confirmed twice before the response arrives', async () => {
      await abrir();
      await confirmar();

      interno().enviar();
      interno().enviar();

      const pedidos = http.match((r) => r.method === 'PUT');
      expect(pedidos).toHaveLength(1);
      pedidos[0].flush(DETALLE);
    });

    it('shows what was saved when the API accepts it', async () => {
      await abrir();
      await confirmar();

      interno().enviar();
      pedidoPut().flush({ ...DETALLE, litros: 1250.5 });
      fixture.detectChanges();

      expect(interno().paso()).toBe('listo');
      expect(texto()).toContain('Recepción corregida');
      expect(texto()).toContain('Litros1.000 → 1.250,5');
      expect(texto()).toContain('Los valores anteriores quedaron en el historial.');
    });
  });

  describe('when the API refuses the correction', () => {
    it('goes back to the form with the message when the new ticket is already used (409)', async () => {
      await abrir();
      await confirmar({ ticket: 5001 });

      interno().enviar();
      pedidoPut().flush({ error: 'El ticket 5001 ya está registrado (Tambo Otro, 03/10/2026)' }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(interno().paso()).toBe('editar');
      expect(texto()).toContain('El ticket 5001 ya está registrado (Tambo Otro, 03/10/2026)');
      expect(interno().enviando()).toBe(false);
    });

    it('says someone else changed the recepción when the version is stale (409)', async () => {
      await abrir();
      await confirmar();

      interno().enviar();
      pedidoPut().flush({ error: 'El registro fue modificado por otra persona.' }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(interno().paso()).toBe('editar');
      expect(texto()).toContain('El registro fue modificado por otra persona. Volvé a abrir la recepción para ver los datos actuales.');
    });

    it('goes back to the form on a 400 and marks the reason when it is the problem', async () => {
      await abrir();
      await confirmar();

      interno().enviar();
      pedidoPut().flush({ error: 'El motivo debe tener al menos 5 caracteres.', field: 'motivo' }, { status: 400, statusText: 'Bad Request' });
      fixture.detectChanges();

      expect(interno().paso()).toBe('editar');
      expect(interno().motivo.touched).toBe(true);
      expect(texto()).toContain('El motivo debe tener al menos 5 caracteres.');
    });

    it('stays on the confirmation and offers to retry when there is no answer (network)', async () => {
      await abrir();
      await confirmar();

      interno().enviar();
      pedidoPut().error(new ProgressEvent('error'), { status: 0 });
      fixture.detectChanges();

      expect(interno().paso()).toBe('confirmar');
      expect(texto()).toContain('No se pudo conectar con el servidor.');
      expect(boton('Reintentar')).toBeTruthy();
    });

    it('says the first send was probably saved when a retry gets a 400 "no changes" (the recepción already has the new values)', async () => {
      await abrir();
      await confirmar();
      interno().enviar();
      pedidoPut().error(new ProgressEvent('error'), { status: 0 });
      fixture.detectChanges();

      interno().enviar();
      pedidoPut().flush({ error: 'No hay cambios para guardar.', field: 'cambios' }, { status: 400, statusText: 'Bad Request' });
      fixture.detectChanges();

      expect(interno().paso()).toBe('confirmar');
      expect(texto()).toContain('Probablemente la corrección ya se guardó');
      expect(boton('Reintentar')).toBeUndefined();
    });

    it('does not say that for a 400 "no changes" on a first send: it goes back to the form', async () => {
      await abrir();
      await confirmar();

      interno().enviar();
      pedidoPut().flush({ error: 'No hay cambios para guardar.', field: 'cambios' }, { status: 400, statusText: 'Bad Request' });
      fixture.detectChanges();

      expect(interno().paso()).toBe('editar');
    });

    it('says the first send was probably saved when a retry gets a 409, and offers no more retries', async () => {
      await abrir();
      await confirmar();
      interno().enviar();
      pedidoPut().error(new ProgressEvent('error'), { status: 0 });
      fixture.detectChanges();

      interno().enviar();
      pedidoPut().flush({ error: 'El registro fue modificado por otra persona.' }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(interno().paso()).toBe('confirmar');
      expect(texto()).toContain('Probablemente la corrección ya se guardó');
      expect(boton('Reintentar')).toBeUndefined();
      expect(boton('Confirmar')).toBeUndefined();
    });
  });

  it('shows text with HTML or SQL typed in the reason or the observaciones as plain text', async () => {
    await abrir();
    cambiar({ observaciones: "<img src=x onerror=alert(1)>'; DROP TABLE Recepciones;--" }, '<script>alert(1)</script> motivo');

    interno().continuar();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el().querySelector('img')).toBeNull();
    expect(el().querySelector('script')).toBeNull();
    expect(texto()).toContain('<script>alert(1)</script> motivo');
    expect(texto()).toContain("<img src=x onerror=alert(1)>'; DROP TABLE Recepciones;--");
  });
});
