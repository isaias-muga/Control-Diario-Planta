import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Login } from './login';
import { tokenDePrueba } from '../../testing/sesion';
import { Rol } from '../../utils/jwt';

describe('Login', () => {
  let component: Login;
  let fixture: ComponentFixture<Login>;
  let http: HttpTestingController;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Login);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('does not show or offer any credentials', () => {
    const texto = fixture.nativeElement.textContent as string;

    expect(texto).not.toContain('credenciales de demo');
    expect(texto).not.toContain('Contraseña: ');
    expect(fixture.nativeElement.querySelector('.demo-credentials')).toBeNull();
  });

  it('does not send an empty form', () => {
    component.submit();

    expect(component.form.touched).toBe(true);
    http.expectNone(() => true);
  });

  it.each<[Rol, string]>([
    ['Recepcion', '/inicio'],
    ['Oficina', '/recepciones'],
    ['Admin', '/recepciones'],
    ['Consulta', '/resumen'],
  ])('a %s that logs in goes to %s', async (rol, destino) => {
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    component.form.setValue({ username: 'ana', password: 'una-clave' });

    component.submit();
    http.expectOne((r) => r.url.endsWith('/Auth/login')).flush({ token: tokenDePrueba(rol), debeCambiarPassword: false });

    expect(navegar).toHaveBeenCalledWith(destino);
  });

  it('a user that must change the password goes to the change page', () => {
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    component.form.setValue({ username: 'ana', password: 'temporal-123' });

    component.submit();
    http.expectOne((r) => r.url.endsWith('/Auth/login')).flush({ token: tokenDePrueba('Oficina', { dcp: '1' }), debeCambiarPassword: true });

    expect(navegar).toHaveBeenCalledWith('/cambiar-password');
  });

  it('shows the wrong credentials message on a 401', () => {
    component.form.setValue({ username: 'ana', password: 'mala' });

    component.submit();
    http.expectOne((r) => r.url.endsWith('/Auth/login')).flush({ error: 'Usuario o contraseña incorrectos.' }, { status: 401, statusText: 'Unauthorized' });

    expect(component.error()).toBe('Usuario o contraseña incorrectos.');
    expect(component.loading()).toBe(false);
  });

  it('shows the message of the API when there are too many attempts (429)', () => {
    component.form.setValue({ username: 'ana', password: 'mala' });

    component.submit();
    http.expectOne((r) => r.url.endsWith('/Auth/login')).flush({ error: 'Demasiados intentos. Probá de nuevo en un minuto.' }, { status: 429, statusText: 'Too Many Requests' });

    expect(component.error()).toBe('Demasiados intentos. Probá de nuevo en un minuto.');
  });

  it('shows a connection message when the server does not answer', () => {
    component.form.setValue({ username: 'ana', password: 'una-clave' });

    component.submit();
    http.expectOne((r) => r.url.endsWith('/Auth/login')).error(new ProgressEvent('error'));

    expect(component.error()).toBe('No se pudo conectar con el servidor.');
  });
});
