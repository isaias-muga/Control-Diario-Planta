import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { AuthService } from './auth';
import { cerrarSesionDePrueba, iniciarSesionDePrueba, tokenDePrueba } from '../testing/sesion';
import { Rol } from '../utils/jwt';

describe('AuthService', () => {
  let dialog: { closeAll: ReturnType<typeof vi.fn> };
  let router: { navigate: ReturnType<typeof vi.fn> };
  let http: HttpTestingController;

  function crear(rol: Rol | null = null, extra: Record<string, unknown> = {}): AuthService {
    if (rol) {
      iniciarSesionDePrueba(rol, extra);
    }
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialog, useValue: dialog },
        { provide: Router, useValue: router },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    return TestBed.inject(AuthService);
  }

  beforeEach(() => {
    cerrarSesionDePrueba();
    vi.useFakeTimers();
    dialog = { closeAll: vi.fn() };
    router = { navigate: vi.fn() };
  });

  afterEach(() => {
    http.verify();
    cerrarSesionDePrueba();
    vi.useRealTimers();
  });

  it('logout closes the open dialogs, clears the token and goes to the login page', () => {
    const service = crear('Admin');

    service.logout();

    expect(dialog.closeAll).toHaveBeenCalledTimes(1);
    expect(localStorage.getItem('dairycontrol_token')).toBeNull();
    expect(service.isLoggedIn()).toBe(false);
    expect(service.rol()).toBeNull();
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });

  it('knows nobody when there is no token', () => {
    const service = crear();

    expect(service.isLoggedIn()).toBe(false);
    expect(service.rol()).toBeNull();
    expect(service.inicio()).toBe('/login');
  });

  it.each<[Rol, string]>([
    ['Admin', '/resumen'],
    ['Oficina', '/resumen'],
    ['Recepcion', '/inicio'],
  ])('a %s starts at %s', (rol, inicio) => {
    const service = crear(rol);

    expect(service.rol()).toBe(rol);
    expect(service.inicio()).toBe(inicio);
    expect(service.esAdmin()).toBe(rol === 'Admin');
    expect(service.esRecepcion()).toBe(rol === 'Recepcion');
    expect(service.nombre()).toBe('Persona de prueba');
  });

  it('a token with a role this client does not know starts at the login page (no redirect loop)', () => {
    localStorage.setItem('dairycontrol_token', tokenDePrueba('Admin', { role: 'Superman' }));
    const service = crear();

    expect(service.rol()).toBeNull();
    expect(service.inicio()).toBe('/login');
  });

  it('a user whose token says the password must be changed starts at the change page', () => {
    const service = crear('Oficina', { dcp: '1' });

    expect(service.debeCambiarPassword()).toBe(true);
    expect(service.inicio()).toBe('/cambiar-password');
  });

  it('login stores the token, learns the role and whether the password must be changed', () => {
    const service = crear();
    const token = tokenDePrueba('Recepcion');

    service.login('recepcion', 'una-clave').subscribe();
    http.expectOne((r) => r.url.endsWith('/Auth/login')).flush({ token, debeCambiarPassword: true });

    expect(localStorage.getItem('dairycontrol_token')).toBe(token);
    expect(service.rol()).toBe('Recepcion');
    expect(service.debeCambiarPassword()).toBe(true);
  });

  it('changing the password replaces the token and lifts the obligation', () => {
    const service = crear('Oficina', { dcp: '1' });
    const nuevo = tokenDePrueba('Oficina');

    service.cambiarPassword('temporal-123', 'mi-clave-nueva').subscribe();
    const req = http.expectOne((r) => r.url.endsWith('/Auth/cambiar-password'));
    expect(req.request.body).toEqual({ passwordActual: 'temporal-123', passwordNueva: 'mi-clave-nueva' });
    req.flush({ token: nuevo, debeCambiarPassword: false });

    expect(service.getToken()).toBe(nuevo);
    expect(service.debeCambiarPassword()).toBe(false);
    expect(service.inicio()).toBe('/resumen');
  });

  it('a 403 that says the password must be changed sends the user to the change page', () => {
    const service = crear('Admin');

    service.marcarDebeCambiarPassword();

    expect(service.debeCambiarPassword()).toBe(true);
    expect(router.navigate).toHaveBeenCalledWith(['/cambiar-password']);
  });

  describe('sliding session', () => {
    it('asks for a fresh token once half of the lifetime has gone, and keeps the new one', () => {
      const service = crear('Recepcion');
      const nuevo = tokenDePrueba('Recepcion', { name: 'Renovado' });

      vi.advanceTimersByTime(5 * 3600 * 1000);
      http.expectNone((r) => r.url.endsWith('/Auth/renovar'));
      vi.advanceTimersByTime(1 * 3600 * 1000 + 1000);
      http.expectOne((r) => r.url.endsWith('/Auth/renovar')).flush({ token: nuevo, debeCambiarPassword: false });

      expect(service.getToken()).toBe(nuevo);
      expect(service.nombre()).toBe('Renovado');
    });

    it('schedules the next renewal after a renewal', () => {
      crear('Admin');
      vi.advanceTimersByTime(6 * 3600 * 1000 + 1000);
      http.expectOne((r) => r.url.endsWith('/Auth/renovar')).flush({ token: tokenDePrueba('Admin'), debeCambiarPassword: false });

      vi.advanceTimersByTime(6 * 3600 * 1000 + 1000);

      http.expectOne((r) => r.url.endsWith('/Auth/renovar')).flush({ token: tokenDePrueba('Admin'), debeCambiarPassword: false });
    });

    it('renews at once a token that is already past half of its life', () => {
      const emitido = Math.floor(Date.now() / 1000) - 10 * 3600;
      localStorage.setItem('dairycontrol_token', tokenDePrueba('Oficina', { iat: emitido, exp: emitido + 12 * 3600 }));
      crear();

      vi.advanceTimersByTime(10);

      http.expectOne((r) => r.url.endsWith('/Auth/renovar')).flush({ token: tokenDePrueba('Oficina'), debeCambiarPassword: false });
    });

    it('does not retry a renewal that the server refused (a 4xx will not change by asking again)', () => {
      crear('Admin');
      vi.advanceTimersByTime(6 * 3600 * 1000 + 1000);
      http.expectOne((r) => r.url.endsWith('/Auth/renovar')).flush({ error: 'No tenés permiso para esta acción.' }, { status: 403, statusText: 'Forbidden' });

      vi.advanceTimersByTime(10 * 60_000);

      http.expectNone((r) => r.url.endsWith('/Auth/renovar'));
    });

    it('does not schedule a renewal for a user who must change the password', () => {
      crear('Oficina', { dcp: '1' });

      vi.advanceTimersByTime(7 * 3600 * 1000);

      http.expectNone((r) => r.url.endsWith('/Auth/renovar'));
    });

    it('does not renew an expired token (the next request gets a 401 and goes to the login page)', () => {
      const emitido = Math.floor(Date.now() / 1000) - 13 * 3600;
      localStorage.setItem('dairycontrol_token', tokenDePrueba('Oficina', { iat: emitido, exp: emitido + 12 * 3600 }));
      crear();

      vi.advanceTimersByTime(10);

      http.expectNone((r) => r.url.endsWith('/Auth/renovar'));
    });

    it('stops renewing after logout', () => {
      const service = crear('Admin');

      service.logout();
      vi.advanceTimersByTime(12 * 3600 * 1000);

      http.expectNone((r) => r.url.endsWith('/Auth/renovar'));
    });

    it('tries again a minute later when the renewal fails for a reason other than the session', () => {
      crear('Admin');
      vi.advanceTimersByTime(6 * 3600 * 1000 + 1000);
      http.expectOne((r) => r.url.endsWith('/Auth/renovar')).error(new ProgressEvent('error'));

      vi.advanceTimersByTime(61_000);

      http.expectOne((r) => r.url.endsWith('/Auth/renovar')).flush({ token: tokenDePrueba('Admin'), debeCambiarPassword: false });
    });
  });
});
