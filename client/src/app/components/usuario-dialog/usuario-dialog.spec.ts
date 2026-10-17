import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { UsuarioDialog } from './usuario-dialog';

describe('UsuarioDialog', () => {
  let fixture: ComponentFixture<UsuarioDialog>;
  let component: UsuarioDialog;
  let http: HttpTestingController;
  let dialogRef: { close: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    dialogRef = { close: vi.fn() };
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: MatDialogRef, useValue: dialogRef }],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(UsuarioDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  function completar(valores: Partial<Record<'username' | 'nombre' | 'rol' | 'password', string>> = {}): void {
    component.form.patchValue({ username: 'maria.perez', nombre: 'María Pérez', rol: 'Oficina', password: 'temporal-123', ...valores } as never);
  }

  it.each([
    ['username', ''],
    ['username', 'ab'],
    ['username', 'con espacio'],
    ['username', 'acentó'],
    ['nombre', '   '],
    ['password', 'corta'],
  ])('does not send with %s = "%s"', (campo, valor) => {
    completar({ [campo]: valor });

    component.crear();

    http.expectNone((r) => r.method === 'POST');
    expect(component.form.touched).toBe(true);
  });

  it('sends the trimmed data once, even when submitted twice, and closes with the new user', () => {
    completar({ username: ' maria.perez ', nombre: ' María Pérez ', rol: 'Recepcion' });
    const creado = { id: 'u1', username: 'maria.perez', nombre: 'María Pérez', rol: 'Recepcion', activo: true, debeCambiarPassword: true };

    component.crear();
    component.crear();
    const req = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/Usuarios'));
    expect(req.request.body).toEqual({ username: 'maria.perez', nombre: 'María Pérez', rol: 'Recepcion', password: 'temporal-123' });
    req.flush(creado);

    expect(dialogRef.close).toHaveBeenCalledExactlyOnceWith(creado);
  });

  it('shows the message of the API next to the username when it is taken (409 has no field: it goes to the general error)', () => {
    completar();

    component.crear();
    http.expectOne((r) => r.method === 'POST').flush({ error: 'Ya existe un usuario con ese nombre de usuario.' }, { status: 409, statusText: 'Conflict' });

    expect(component.saveError()).toBe('Ya existe un usuario con ese nombre de usuario.');
    expect(component.saving()).toBe(false);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('shows a 400 of a field next to that field', () => {
    completar();

    component.crear();
    http.expectOne((r) => r.method === 'POST').flush({ error: 'La contraseña debe tener al menos 8 caracteres.', field: 'password' }, { status: 400, statusText: 'Bad Request' });

    expect(component.saveError()).toBeNull();
    expect(component.errorCampo()).toEqual({ campo: 'password', mensaje: 'La contraseña debe tener al menos 8 caracteres.' });
  });

  it('offers the three roles with plain names', () => {
    const texto = fixture.nativeElement.textContent as string;

    expect(texto).toContain('Nuevo usuario');
    expect(component['roles']).toEqual(['Admin', 'Oficina', 'Recepcion']);
  });
});
