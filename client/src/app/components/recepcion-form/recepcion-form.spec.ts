import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RecepcionForm } from './recepcion-form';
import { ahoraEnPlanta } from '../../utils/periodos';

describe('RecepcionForm', () => {
  let component: RecepcionForm;
  let fixture: ComponentFixture<RecepcionForm>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RecepcionForm],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RecepcionForm);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('proveedorId', 'test-id');
    await fixture.whenStable();
    responderIniciales();
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  const LIMITES = { acidezMaxima: 19, temperaturaMaxima: 20, aguaMaxima: 0, actualizadoEn: '2026-10-15T00:00:00Z' };

  /** The form asks for the suggested ticket and the quality limits when it is created. */
  function responderIniciales(ticket = 4981): void {
    http.expectOne((r) => r.url.endsWith('/Recepciones/siguiente-ticket')).flush({ ticket });
    http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush(LIMITES);
  }

  /** Answers (and discards) whatever the form requested on creation, for tests that build a second component. */
  function descartarSolicitudesIniciales(): void {
    http.match(() => true);
  }

  function completarObligatorios(): void {
    component.form.patchValue({ litros: 1000, acidez: 17, temperatura: 5 });
  }

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('renders the optional % agua and Ticket fields', () => {
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#aguaPorcentaje')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('#ticket')).toBeTruthy();
  });

  describe('optional fields', () => {
    const etiquetas = (): string[] =>
      Array.from(
        fixture.nativeElement.querySelectorAll('mat-form-field mat-label') as NodeListOf<HTMLElement>,
      ).map((l) => (l.textContent ?? '').trim());

    it('marks the optional fields in the label', () => {
      fixture.detectChanges();

      expect(etiquetas()).toEqual(
        expect.arrayContaining([
          'Grasa (%) (opcional)',
          'Silo (opcional)',
          '% agua (opcional)',
          'Ticket (opcional)',
          'Observaciones (opcional)',
        ]),
      );
    });

    it('does not mark the required fields as optional', () => {
      fixture.detectChanges();

      const requeridos = etiquetas().filter((e) =>
        ['Fecha y hora', 'Litros', 'Acidez', 'Temperatura (°C)'].includes(e),
      );

      expect(requeridos).toHaveLength(4);
    });

    it('does not use mat-hint for "opcional"', () => {
      fixture.detectChanges();

      // The only hint is the short help text about the suggested ticket
      const pistas = Array.from(fixture.nativeElement.querySelectorAll('mat-hint') as NodeListOf<HTMLElement>);
      expect(pistas.map((h) => h.textContent)).not.toContainEqual(expect.stringMatching(/opcional/));
      expect(fixture.nativeElement.textContent).not.toMatch(/\bopcional\b(?!\))/);
    });
  });

  describe('plausible ranges (typing errors, not quality limits)', () => {
    type Campo = 'litros' | 'grasa' | 'acidez' | 'temperatura' | 'silo' | 'aguaPorcentaje';

    const control = (campo: Campo) => component.form.controls[campo];

    // [field, value just inside / on the edge, value just outside]
    const dentro: [Campo, number][] = [
      ['litros', 0.001], ['litros', 30000],
      ['grasa', 1], ['grasa', 10],
      ['acidez', 10], ['acidez', 30],
      ['temperatura', 0], ['temperatura', 45],
      ['silo', 1], ['silo', 20],
      ['aguaPorcentaje', 0], ['aguaPorcentaje', 100],
    ];
    const fuera: [Campo, number][] = [
      ['litros', 0], ['litros', -1], ['litros', 0.0009], ['litros', 30000.01],
      ['grasa', 0.99], ['grasa', 10.01],
      ['acidez', 9.99], ['acidez', 30.01],
      ['temperatura', -0.01], ['temperatura', 45.01],
      ['silo', 0], ['silo', 21],
      ['aguaPorcentaje', -0.01], ['aguaPorcentaje', 100.01],
    ];

    it.each(dentro)('accepts %s = %s', (campo, valor) => {
      completarObligatorios();
      control(campo).setValue(valor);

      expect(control(campo).valid).toBe(true);
    });

    it.each(fuera)('rejects %s = %s', (campo, valor) => {
      completarObligatorios();
      control(campo).setValue(valor);

      expect(control(campo).invalid).toBe(true);
      expect(component.form.invalid).toBe(true);
    });

    it('does not require the optional grasa and silo', () => {
      completarObligatorios();

      expect(control('grasa').valid).toBe(true);
      expect(control('silo').valid).toBe(true);
    });

    it('rejects a non-integer silo', () => {
      completarObligatorios();
      control('silo').setValue(2.5);

      expect(control('silo').invalid).toBe(true);
    });

    it('does not submit an impossible value', () => {
      completarObligatorios();
      control('grasa').setValue(12);

      component.submit();

      http.expectNone((r) => r.url.endsWith('/recepciones'));
    });

    it('shows a clear message next to each field with an impossible value', () => {
      component.form.patchValue({
        litros: 40000, grasa: 12, acidez: 5, temperatura: 60, silo: 99, aguaPorcentaje: 150,
      });
      component.form.markAllAsTouched();
      fixture.detectChanges();

      const texto = fixture.nativeElement.textContent as string;

      expect(texto).toContain('Entre 0,001 y 30.000.');
      expect(texto).toContain('Entre 1 y 10 %.');
      expect(texto).toContain('Requerido, entre 10 y 30 °D.');
      expect(texto).toContain('Requerido, entre 0 y 45 °C.');
      expect(texto).toContain('Número entero entre 1 y 20.');
      expect(texto).toContain('Entre 0 y 100.');
    });
  });

  it('is valid without water and ticket', () => {
    completarObligatorios();

    expect(component.form.valid).toBe(true);
  });

  it.each([-0.01, 100.01])('rejects % agua %s', (valor) => {
    completarObligatorios();
    component.form.controls.aguaPorcentaje.setValue(valor);

    expect(component.form.controls.aguaPorcentaje.invalid).toBe(true);
  });

  it.each([0, 100])('accepts % agua %s', (valor) => {
    completarObligatorios();
    component.form.controls.aguaPorcentaje.setValue(valor);

    expect(component.form.controls.aguaPorcentaje.valid).toBe(true);
  });

  it.each([0, -1, 1.5, 2147483648])('rejects ticket %s', (valor) => {
    completarObligatorios();
    component.form.controls.ticket.setValue(valor);

    expect(component.form.controls.ticket.invalid).toBe(true);
  });

  it('accepts the largest int as ticket', () => {
    completarObligatorios();
    component.form.controls.ticket.setValue(2147483647);

    expect(component.form.controls.ticket.valid).toBe(true);
  });

  it('sends only one request when submitted twice before the response arrives', () => {
    completarObligatorios();

    component.submit();
    component.submit();

    const requests = http.match((r) => r.url.endsWith('/recepciones'));
    expect(requests).toHaveLength(1);
    requests[0].flush({});
  });

  it('sends null water and ticket when left empty', () => {
    completarObligatorios();
    component.form.controls.ticket.setValue(null); // the suggested ticket was cleared

    component.submit();

    const req = http.expectOne((r) => r.url.endsWith('/Proveedores/test-id/recepciones'));
    expect(req.request.body.aguaPorcentaje).toBeNull();
    expect(req.request.body.ticket).toBeNull();
  });

  describe('numbers typed the es-AR way', () => {
    const escribir = (id: string, texto: string): HTMLInputElement => {
      const campo = fixture.nativeElement.querySelector(`#${id}`) as HTMLInputElement;
      campo.value = texto;
      campo.dispatchEvent(new Event('input'));
      campo.dispatchEvent(new Event('blur'));
      fixture.detectChanges();
      return campo;
    };

    it('uses text inputs, never type=number', () => {
      fixture.detectChanges();

      for (const id of ['ticket', 'litros', 'aguaPorcentaje', 'acidez', 'temperatura', 'grasa', 'silo']) {
        expect((fixture.nativeElement.querySelector(`#${id}`) as HTMLInputElement).type).toBe('text');
      }
      expect(fixture.nativeElement.querySelector('input[type=number]')).toBeNull();
    });

    it('sends litros "1.234,5" as 1234.5 and shows it as 1.234,5', () => {
      fixture.detectChanges();
      const litros = escribir('litros', '1.234,5');
      escribir('acidez', '17,5');
      escribir('temperatura', '5');

      component.submit();

      const req = http.expectOne((r) => r.url.endsWith('/Proveedores/test-id/recepciones'));
      expect(req.request.body.litros).toBe(1234.5);
      expect(req.request.body.acidez).toBe(17.5);
      expect(litros.value).toBe('1.234,5');
    });

    it('does not read "1.234,5" acidez as 1.2345: it is out of range and nothing is sent', () => {
      fixture.detectChanges();
      escribir('litros', '1000');
      escribir('acidez', '1.234,5');
      escribir('temperatura', '5');

      component.submit();

      http.expectNone((r) => r.url.endsWith('/Proveedores/test-id/recepciones'));
      expect(component.form.controls.acidez.value).toBe(1234.5);
      expect(component.form.controls.acidez.errors?.['max']).toBeDefined();
    });

    it('does not send an ambiguous litros and shows the parser message', () => {
      fixture.detectChanges();
      escribir('litros', '1.2.3');
      escribir('acidez', '17');
      escribir('temperatura', '5');

      component.submit();
      fixture.detectChanges();

      http.expectNone((r) => r.url.endsWith('/Proveedores/test-id/recepciones'));
      expect(fixture.nativeElement.textContent).toContain('Usá punto para los miles');
    });

    it.each(['ticket', 'silo'])('rejects a decimal %s with the integer message', (id) => {
      fixture.detectChanges();

      escribir(id, '2,5');

      expect(component.form.getRawValue()[id as 'ticket' | 'silo']).toBeNull();
      expect(fixture.nativeElement.textContent).toContain('Ingresá un número entero, sin decimales.');
    });

    it('counts text the parser rejected as data to lose when closing', () => {
      fixture.detectChanges();
      expect(component.tieneDatos()).toBe(false);

      escribir('litros', '1.2.3');

      expect(component.form.controls.litros.value).toBeNull();
      expect(component.tieneDatos()).toBe(true);
    });

    it('never overwrites a rejected ticket text with the suggestion that arrives later', () => {
      descartarSolicitudesIniciales();
      const otro = TestBed.createComponent(RecepcionForm);
      otro.componentRef.setInput('proveedorId', 'test-id');
      otro.detectChanges();
      const campo = otro.nativeElement.querySelector('#ticket') as HTMLInputElement;
      campo.value = '2,5';
      campo.dispatchEvent(new Event('input'));

      responderIniciales(4981);

      expect(otro.componentInstance.form.controls.ticket.errors?.['numero']).toBeDefined();
      expect(campo.value).toBe('2,5');
    });

    it('is disabled with the form', () => {
      fixture.detectChanges();

      component.form.disable();

      expect((fixture.nativeElement.querySelector('#litros') as HTMLInputElement).disabled).toBe(true);
    });

    it('shows the suggested ticket plain, without thousands separators', () => {
      fixture.detectChanges();
      component.form.controls.ticket.setValue(1000001);

      expect((fixture.nativeElement.querySelector('#ticket') as HTMLInputElement).value).toBe('1000001');
    });
  });

  it('sends water and ticket when filled', () => {
    completarObligatorios();
    component.form.patchValue({ aguaPorcentaje: 2.5, ticket: 4501 });

    component.submit();

    const req = http.expectOne((r) => r.url.endsWith('/Proveedores/test-id/recepciones'));
    expect(req.request.body.aguaPorcentaje).toBe(2.5);
    expect(req.request.body.ticket).toBe(4501);
  });

  describe('duplicate ticket (409)', () => {
    const mensaje = 'El ticket 6311 ya está registrado (Tambo Las Acacias, 11/10/2026)';

    function enviarConTicketDuplicado(): void {
      completarObligatorios();
      component.form.patchValue({ ticket: 6311 });
      component.submit();
      http
        .expectOne((r) => r.url.endsWith('/Proveedores/test-id/recepciones'))
        .flush({ error: mensaje }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();
    }

    it('shows the message next to the ticket field, not as a generic error', () => {
      enviarConTicketDuplicado();

      const campo = fixture.nativeElement.querySelector('#ticket').closest('mat-form-field');
      expect(campo.textContent).toContain(mensaje);
      expect(component.saveError()).toBeNull();
      expect(component.saving()).toBe(false);
    });

    it('keeps the entered values (the form is not reset)', () => {
      enviarConTicketDuplicado();

      expect(component.form.controls.ticket.value).toBe(6311);
      expect(component.form.controls.litros.value).toBe(1000);
    });

    it('clears the ticket error when the user edits the ticket', () => {
      enviarConTicketDuplicado();

      component.form.controls.ticket.setValue(6312);

      expect(component.form.controls.ticket.errors).toBeNull();
    });
  });

  describe('fechaHora', () => {
    it('defaults to the plant time even when the browser is in another time zone', () => {
      vi.useFakeTimers();
      try {
        // 01:30 UTC on the 14th is 22:30 on the 13th in Buenos Aires
        vi.setSystemTime(new Date('2026-10-14T01:30:00Z'));
        const otro = TestBed.createComponent(RecepcionForm);
        otro.componentRef.setInput('proveedorId', 'test-id');
        descartarSolicitudesIniciales();

        expect(otro.componentInstance.form.controls.fechaHora.value).toBe('2026-10-13T22:30');
      } finally {
        vi.useRealTimers();
      }
    });

    it.each([
      ['1999-12-31T23:59', 'fechaMin'],
      ['1900-01-01T00:00', 'fechaMin'],
      ['2099-01-01T00:00', 'fechaMax'],
      ['9999-12-31T23:59', 'fechaMax'],
    ])('rejects %s before sending', (valor, error) => {
      completarObligatorios();
      component.form.controls.fechaHora.setValue(valor);

      component.submit();

      expect(component.form.controls.fechaHora.errors).toEqual({ [error]: true });
      http.expectNone((r) => r.url.endsWith('/Proveedores/test-id/recepciones'));
    });

    it('accepts 2000-01-01 and a time within the next hour', () => {
      completarObligatorios();
      component.form.controls.fechaHora.setValue('2000-01-01T00:00');
      expect(component.form.controls.fechaHora.valid).toBe(true);

      component.form.controls.fechaHora.setValue(ahoraEnPlanta(new Date(Date.now() + 30 * 60_000)));
      expect(component.form.controls.fechaHora.valid).toBe(true);
    });

    it('rejects a time more than an hour ahead', () => {
      component.form.controls.fechaHora.setValue(ahoraEnPlanta(new Date(Date.now() + 90 * 60_000)));

      expect(component.form.controls.fechaHora.errors).toEqual({ fechaMax: true });
    });

    it('shows a 400 that names the field next to it, not as a generic error', () => {
      completarObligatorios();
      component.submit();
      http
        .expectOne((r) => r.url.endsWith('/Proveedores/test-id/recepciones'))
        .flush(
          { error: 'La fecha y hora es obligatoria.', field: 'fechaHora' },
          { status: 400, statusText: 'Bad Request' },
        );
      fixture.detectChanges();

      const campo = fixture.nativeElement.querySelector('#fechaHora').closest('mat-form-field');
      expect(campo.textContent).toContain('La fecha y hora es obligatoria.');
      expect(component.saveError()).toBeNull();
    });
  });

  it('shows other API errors in the generic form error', () => {
    completarObligatorios();

    component.submit();
    http
      .expectOne((r) => r.url.endsWith('/Proveedores/test-id/recepciones'))
      .flush({ error: 'Dato inválido' }, { status: 400, statusText: 'Bad Request' });

    expect(component.saveError()).toBe('Dato inválido');
    expect(component.form.controls.ticket.errors).toBeNull();
  });

  it('does not submit when water is out of range', () => {
    completarObligatorios();
    component.form.controls.aguaPorcentaje.setValue(150);

    component.submit();

    http.expectNone((r) => r.url.endsWith('/recepciones'));
  });

  describe('initial focus when the dialog opens', () => {
    it('marks Litros as the initial focus when the proveedor is given (the ticket is prefilled)', () => {
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('#litros').hasAttribute('cdkFocusInitial')).toBe(true);
      expect(fixture.nativeElement.querySelector('#ticket').hasAttribute('cdkFocusInitial')).toBe(false);
    });

    it('marks the proveedor selector instead when the proveedor must be chosen', async () => {
      const sinProveedor = TestBed.createComponent(RecepcionForm);
      await sinProveedor.whenStable();
      http.match((r) => r.url.endsWith('/Proveedores/opciones'));
      responderIniciales();
      sinProveedor.detectChanges();

      expect(sinProveedor.nativeElement.querySelector('#proveedorId').hasAttribute('cdkFocusInitial')).toBe(true);
      expect(sinProveedor.nativeElement.querySelector('#litros').hasAttribute('cdkFocusInitial')).toBe(false);
    });
  });

  describe('suggested ticket', () => {
    it('prefills the ticket with the suggestion and says it can be changed', () => {
      fixture.detectChanges();

      expect(component.form.controls.ticket.value).toBe(4981);
      expect(fixture.nativeElement.querySelector('mat-hint')?.textContent).toContain(
        'Siguiente número sugerido; se puede cambiar.',
      );
    });

    it('hides the hint once the operator edits the ticket, and sends the edited value', () => {
      fixture.detectChanges();
      const input = fixture.nativeElement.querySelector('#ticket') as HTMLInputElement;
      input.value = '5000';
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      completarObligatorios();

      component.submit();

      expect(fixture.nativeElement.querySelector('mat-hint')).toBeNull();
      const req = http.expectOne((r) => r.url.endsWith('/Proveedores/test-id/recepciones'));
      expect(req.request.body.ticket).toBe(5000);
    });

    it('never overwrites a ticket typed before the suggestion arrives', () => {
      const otro = TestBed.createComponent(RecepcionForm);
      otro.componentRef.setInput('proveedorId', 'test-id');
      otro.componentInstance.form.controls.ticket.setValue(777);

      responderIniciales(4981);

      expect(otro.componentInstance.form.controls.ticket.value).toBe(777);
    });

    it('leaves the ticket empty when there is nothing to suggest', () => {
      const otro = TestBed.createComponent(RecepcionForm);
      otro.componentRef.setInput('proveedorId', 'test-id');
      http.expectOne((r) => r.url.endsWith('/Recepciones/siguiente-ticket')).flush({ ticket: null });
      http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush(LIMITES);

      expect(otro.componentInstance.form.controls.ticket.value).toBeNull();
      expect(otro.componentInstance.ticketSugerido()).toBe(false);
    });

    it('leaves the ticket empty when the suggestion cannot be loaded', () => {
      const otro = TestBed.createComponent(RecepcionForm);
      otro.componentRef.setInput('proveedorId', 'test-id');
      http
        .expectOne((r) => r.url.endsWith('/Recepciones/siguiente-ticket'))
        .flush({}, { status: 500, statusText: 'Server Error' });
      http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush(LIMITES);

      expect(otro.componentInstance.form.controls.ticket.value).toBeNull();
      expect(otro.componentInstance.ticketSugerido()).toBe(false);
    });
  });

  describe('live preview', () => {
    it('shows the real liters (liters minus the water) as the operator types', () => {
      component.form.patchValue({ litros: 1905, aguaPorcentaje: 1.5 });
      fixture.detectChanges();

      expect(component.litrosRealesPrevios()).toBe(1876.425);
      expect(fixture.nativeElement.querySelector('.resumen-vivo').textContent).toContain('Litros reales:');
    });

    it('counts a missing water as 0 and shows a dash until the liters are valid', () => {
      expect(component.litrosRealesPrevios()).toBeNull();

      component.form.patchValue({ litros: 2150 });

      expect(component.litrosRealesPrevios()).toBe(2150);
    });

    it('says the reading is within the limits when no limit is exceeded', () => {
      component.form.patchValue({ litros: 2150, acidez: 18, temperatura: 5.2 });
      fixture.detectChanges();

      expect(component.motivosPrevios()).toEqual([]);
      expect(fixture.nativeElement.querySelector('.estado-ok').textContent).toContain('Dentro de los límites');
    });

    it('names every limit that is exceeded, using the limits from the API', () => {
      component.form.patchValue({ litros: 2150, acidez: 20, temperatura: 21.3, aguaPorcentaje: 1.5 });
      fixture.detectChanges();

      expect(component.motivosPrevios()).toEqual(['Acidez', 'Temperatura', 'Agua']);
      expect(fixture.nativeElement.querySelector('.estado-alerta').textContent).toContain(
        'Fuera de los límites: acidez, temperatura, agua',
      );
    });

    it('shows no limits check while the limits are not loaded', () => {
      const otro = TestBed.createComponent(RecepcionForm);
      otro.componentRef.setInput('proveedorId', 'test-id');
      http.expectOne((r) => r.url.endsWith('/Recepciones/siguiente-ticket')).flush({ ticket: 1 });
      http.expectOne((r) => r.url.endsWith('/Configuracion/calidad')).flush({}, { status: 500, statusText: 'Server Error' });
      otro.componentInstance.form.patchValue({ acidez: 25 });

      expect(otro.componentInstance.motivosPrevios()).toBeNull();
    });
  });

  describe('without a proveedor (chosen in the form)', () => {
    let sinProveedor: ComponentFixture<RecepcionForm>;

    beforeEach(async () => {
      sinProveedor = TestBed.createComponent(RecepcionForm);
      await sinProveedor.whenStable();
      http
        .expectOne((r) => r.url.endsWith('/Proveedores/opciones'))
        .flush([
          { id: 'b', nombre: 'Tambo San José', cantidadRecepciones: 1 },
          { id: 'a', nombre: 'Tambo Don Pedro', cantidadRecepciones: 2 },
        ]);
      responderIniciales();
      sinProveedor.detectChanges();
    });

    it('shows a required proveedor selector with the proveedores sorted by name', () => {
      const componente = sinProveedor.componentInstance;

      expect(sinProveedor.nativeElement.querySelector('#proveedorId')).toBeTruthy();
      expect(componente.proveedores().map((p) => p.nombre)).toEqual(['Tambo Don Pedro', 'Tambo San José']);
      expect(componente.form.controls.proveedorId.hasError('required')).toBe(true);
    });

    it('does not submit until a proveedor is chosen', () => {
      const componente = sinProveedor.componentInstance;
      componente.form.patchValue({ litros: 1000, acidez: 17, temperatura: 5 });

      componente.submit();

      expect(componente.form.invalid).toBe(true);
      http.expectNone((r) => r.url.endsWith('/recepciones'));
    });

    it('registers the recepción for the chosen proveedor', () => {
      const componente = sinProveedor.componentInstance;
      componente.form.patchValue({ proveedorId: 'b', litros: 1000, acidez: 17, temperatura: 5 });

      componente.submit();

      const req = http.expectOne((r) => r.url.endsWith('/Proveedores/b/recepciones'));
      expect(req.request.method).toBe('POST');
      req.flush({});
    });

    it('shows no selector when the proveedor is given', () => {
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('#proveedorId')).toBeNull();
    });
  });

  describe('"Registrar y cargar otra"', () => {
    function enviarYResponder(cargarOtra: boolean): void {
      component.submit(cargarOtra);
      http.expectOne((r) => r.url.endsWith('/Proveedores/test-id/recepciones')).flush({});
    }

    it('keeps the proveedor, clears the fields and suggests the next ticket', () => {
      completarObligatorios();
      component.form.patchValue({ grasa: 3.5, silo: 2, aguaPorcentaje: 1, observaciones: 'Olor' });

      enviarYResponder(true);
      http.expectOne((r) => r.url.endsWith('/Recepciones/siguiente-ticket')).flush({ ticket: 4982 });

      const v = component.form.getRawValue();
      expect(v.proveedorId).toBe('test-id');
      expect(v.litros).toBeNull();
      expect(v.acidez).toBeNull();
      expect(v.temperatura).toBeNull();
      expect(v.grasa).toBeNull();
      expect(v.silo).toBeNull();
      expect(v.aguaPorcentaje).toBeNull();
      expect(v.observaciones).toBe('');
      expect(v.ticket).toBe(4982);
      expect(component.saving()).toBe(false);
    });

    it('registers two recepciones with consecutive suggested tickets', () => {
      completarObligatorios();

      component.submit(true);
      const req1 = http.expectOne((r) => r.url.endsWith('/Proveedores/test-id/recepciones'));
      req1.flush({});
      http.expectOne((r) => r.url.endsWith('/Recepciones/siguiente-ticket')).flush({ ticket: 4982 });
      completarObligatorios();
      component.submit(true);
      const req2 = http.expectOne((r) => r.url.endsWith('/Proveedores/test-id/recepciones'));
      req2.flush({});
      http.expectOne((r) => r.url.endsWith('/Recepciones/siguiente-ticket')).flush({ ticket: 4983 });

      expect(req1.request.body.ticket).toBe(4981);
      expect(req2.request.body.ticket).toBe(4982);
      expect(component.form.controls.ticket.value).toBe(4983);
    });

    it('emits true for "cargar otra" and false for a plain "Registrar"', () => {
      const emitidos: boolean[] = [];
      component.registrada.subscribe((cargarOtra) => emitidos.push(cargarOtra));
      completarObligatorios();

      enviarYResponder(true);
      http.expectOne((r) => r.url.endsWith('/Recepciones/siguiente-ticket')).flush({ ticket: 4982 });
      completarObligatorios();
      enviarYResponder(false);

      expect(emitidos).toEqual([true, false]);
    });

    it('does not ask for another suggestion after a plain "Registrar"', () => {
      completarObligatorios();

      enviarYResponder(false);

      http.expectNone((r) => r.url.endsWith('/Recepciones/siguiente-ticket'));
    });

    it('keeps the entered values when the save fails', () => {
      completarObligatorios();

      component.submit(true);
      http
        .expectOne((r) => r.url.endsWith('/Proveedores/test-id/recepciones'))
        .flush({ error: 'Dato inválido' }, { status: 400, statusText: 'Bad Request' });

      expect(component.form.controls.litros.value).toBe(1000);
      expect(component.saveError()).toBe('Dato inválido');
    });

    it('sends only one request when both buttons are clicked before the response arrives', () => {
      completarObligatorios();

      component.submit(true);
      component.submit(false);
      component.submit(true);

      const requests = http.match((r) => r.url.endsWith('/recepciones'));
      expect(requests).toHaveLength(1);
      requests[0].flush({});
      http.match((r) => r.url.endsWith('/Recepciones/siguiente-ticket'));
    });

    it('disables both buttons while saving', () => {
      completarObligatorios();
      component.submit(true);
      fixture.detectChanges();

      const botones = fixture.nativeElement.querySelectorAll('.actions button') as NodeListOf<HTMLButtonElement>;
      expect(Array.from(botones).every((b) => b.disabled)).toBe(true);
      http.match((r) => r.url.endsWith('/recepciones'));
    });
  });
});
