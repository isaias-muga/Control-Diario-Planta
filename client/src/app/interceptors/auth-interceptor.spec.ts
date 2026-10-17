import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { authInterceptor } from './auth-interceptor';
import { AuthService } from '../services/auth';
import { environment } from '../../environments/environment';
import { cerrarSesionDePrueba, iniciarSesionDePrueba } from '../testing/sesion';

describe('authInterceptor', () => {
  const url = `${environment.apiUrl}/Proveedores`;
  let http: HttpClient;
  let httpTesting: HttpTestingController;
  let router: { navigate: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    iniciarSesionDePrueba('Oficina');
    router = { navigate: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: Router, useValue: router },
        { provide: MatDialog, useValue: { closeAll: vi.fn() } },
      ],
    });
    http = TestBed.inject(HttpClient);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTesting.verify();
    cerrarSesionDePrueba();
  });

  it('adds the token to the API requests', () => {
    http.get(url).subscribe();

    const req = httpTesting.expectOne(url);

    expect(req.request.headers.get('Authorization')).toBe(`Bearer ${TestBed.inject(AuthService).getToken()}`);
    req.flush([]);
  });

  it('a 401 ends the session and goes to the login page', () => {
    http.get(url).subscribe({ error: () => undefined });

    httpTesting.expectOne(url).flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(TestBed.inject(AuthService).isLoggedIn()).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });

  it('a 403 that says the password must be changed sends the user to the change page, keeping the session', () => {
    http.get(url).subscribe({ error: () => undefined });

    httpTesting.expectOne(url)
      .flush({ error: 'Tenés que cambiar la contraseña antes de continuar.', code: 'debeCambiarPassword' }, { status: 403, statusText: 'Forbidden' });

    const auth = TestBed.inject(AuthService);
    expect(auth.isLoggedIn()).toBe(true);
    expect(auth.debeCambiarPassword()).toBe(true);
    expect(router.navigate).toHaveBeenCalledWith(['/cambiar-password']);
  });

  it('any other 403 changes nothing (the page shows its own message)', () => {
    http.get(url).subscribe({ error: () => undefined });

    httpTesting.expectOne(url).flush({ error: 'No tenés permiso para esta acción.' }, { status: 403, statusText: 'Forbidden' });

    expect(TestBed.inject(AuthService).isLoggedIn()).toBe(true);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('does not touch requests to other servers', () => {
    http.get('https://otro.example/datos').subscribe();

    const req = httpTesting.expectOne('https://otro.example/datos');

    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });
});
