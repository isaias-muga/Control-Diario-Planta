import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { OperarioDialog } from './operario-dialog';

describe('OperarioDialog', () => {
  let fixture: ComponentFixture<OperarioDialog>;
  let component: OperarioDialog;
  let http: HttpTestingController;
  let dialogRef: { close: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    dialogRef = { close: vi.fn() };
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: MatDialogRef, useValue: dialogRef }],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(OperarioDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  it('masks the PIN and asks for a numeric keyboard', () => {
    const pin = fixture.nativeElement.querySelector('#pin') as HTMLInputElement;

    expect(pin.type).toBe('password');
    expect(pin.getAttribute('inputmode')).toBe('numeric');
    expect(pin.maxLength).toBe(4);
  });

  it.each(['', '123', '12345', 'abcd', '12 4', '-123'])('does not send the PIN "%s"', (pin) => {
    component.form.setValue({ nombre: 'Juan Pérez', pin });

    component.crear();

    http.expectNone((r) => r.method === 'POST');
  });

  it('does not send a blank name', () => {
    component.form.setValue({ nombre: '   ', pin: '1234' });

    component.crear();

    http.expectNone((r) => r.method === 'POST');
  });

  it('sends the operario once and closes with it', () => {
    component.form.setValue({ nombre: ' Juan Pérez ', pin: '1234' });
    const creado = { id: 'o1', nombre: 'Juan Pérez', activo: true };

    component.crear();
    component.crear();
    const req = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/Operarios'));
    expect(req.request.body).toEqual({ nombre: 'Juan Pérez', pin: '1234' });
    req.flush(creado);

    expect(dialogRef.close).toHaveBeenCalledExactlyOnceWith(creado);
  });

  it('shows a PIN that another operario already uses next to the PIN field', () => {
    component.form.setValue({ nombre: 'Juan Pérez', pin: '1234' });

    component.crear();
    http.expectOne((r) => r.method === 'POST').flush({ error: 'Ese PIN ya lo usa otro operario. Elegí otro.' }, { status: 409, statusText: 'Conflict' });

    expect(component.errorPin()).toBe('Ese PIN ya lo usa otro operario. Elegí otro.');
    expect(dialogRef.close).not.toHaveBeenCalled();
  });
});
