import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CambiarPassword } from './cambiar-password';
import { iniciarSesionDePrueba, tokenDePrueba } from '../../testing/sesion';

describe('CambiarPassword', () => {
  let fixture: ComponentFixture<CambiarPassword>;
  let component: CambiarPassword;
  let http: HttpTestingController;

  beforeEach(async () => {
    iniciarSesionDePrueba('Oficina', { dcp: '1' });
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CambiarPassword);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
  });

  function completar(actual = 'temporal-123', nueva = 'mi-clave-nueva', repetir = nueva): void {
    component.form.setValue({ passwordActual: actual, passwordNueva: nueva, repetir });
  }

  it('says the change is required when the user must change the password', () => {
    expect(fixture.nativeElement.textContent).toContain('Tenés que elegir una contraseña nueva antes de seguir.');
  });

  it('does not send a short password or two that differ', () => {
    completar('temporal-123', 'corta');
    component.submit();
    completar('temporal-123', 'mi-clave-nueva', 'otra-distinta');
    component.submit();

    http.expectNone(() => true);
    expect(component.form.hasError('noCoinciden')).toBe(true);
  });

  it('sends the change once, even when it is submitted twice, and goes on', () => {
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    completar();

    component.submit();
    component.submit();
    http.expectOne((r) => r.url.endsWith('/Auth/cambiar-password')).flush({ token: tokenDePrueba('Oficina'), debeCambiarPassword: false });

    expect(navegar).toHaveBeenCalledTimes(1);
  });

  it('shows the message of the API next to the current password when it is wrong', () => {
    completar('mala');

    component.submit();
    http.expectOne((r) => r.url.endsWith('/Auth/cambiar-password'))
      .flush({ error: 'La contraseña actual no es correcta.', field: 'passwordActual' }, { status: 400, statusText: 'Bad Request' });

    expect(component.errorActual()).toBe('La contraseña actual no es correcta.');
    expect(component.guardando()).toBe(false);
  });

  it('shows the message of the API next to the new password when it is rejected', () => {
    completar();

    component.submit();
    http.expectOne((r) => r.url.endsWith('/Auth/cambiar-password'))
      .flush({ error: 'La contraseña nueva debe ser distinta de la actual.', field: 'passwordNueva' }, { status: 400, statusText: 'Bad Request' });

    expect(component.errorNueva()).toBe('La contraseña nueva debe ser distinta de la actual.');
  });
});
