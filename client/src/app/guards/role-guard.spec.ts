import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, GuardResult, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { cambioPasswordGuard, roleGuard } from './role-guard';
import { cerrarSesionDePrueba, iniciarSesionDePrueba } from '../testing/sesion';
import { Rol } from '../utils/jwt';

describe('role guards', () => {
  beforeEach(() => {
    cerrarSesionDePrueba();
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
  });

  afterEach(() => cerrarSesionDePrueba());

  function ejecutar(guard: ReturnType<typeof roleGuard> | typeof cambioPasswordGuard): GuardResult {
    return TestBed.runInInjectionContext(() => guard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)) as GuardResult;
  }

  const destino = (resultado: GuardResult): string => TestBed.inject(Router).serializeUrl(resultado as UrlTree);

  it('sends someone who is not logged in to the login page', () => {
    expect(destino(ejecutar(roleGuard('Admin')))).toBe('/login');
  });

  it.each<[Rol, Rol[]]>([
    ['Admin', ['Admin', 'Oficina']],
    ['Oficina', ['Admin', 'Oficina']],
    ['Consulta', ['Admin', 'Oficina', 'Consulta']],
    ['Recepcion', ['Recepcion']],
  ])('lets a %s in to a route for %j', (rol, permitidos) => {
    iniciarSesionDePrueba(rol);

    expect(ejecutar(roleGuard(...permitidos))).toBe(true);
  });

  it('sends the reception phone home when it opens an office page', () => {
    iniciarSesionDePrueba('Recepcion');

    expect(destino(ejecutar(roleGuard('Admin', 'Oficina')))).toBe('/inicio');
  });

  it('sends the office to Recepciones when it opens an admin page', () => {
    iniciarSesionDePrueba('Oficina');

    expect(destino(ejecutar(roleGuard('Admin')))).toBe('/recepciones');
  });

  it('sends Consulta to the dashboard when it opens the registration flow', () => {
    iniciarSesionDePrueba('Consulta');

    expect(destino(ejecutar(roleGuard('Admin', 'Oficina', 'Recepcion')))).toBe('/resumen');
  });

  it('sends the admin to Recepciones when it opens the reception home', () => {
    iniciarSesionDePrueba('Admin');

    expect(destino(ejecutar(roleGuard('Recepcion')))).toBe('/recepciones');
  });

  it('sends a token with a role the client does not know to the login page instead of looping', () => {
    iniciarSesionDePrueba('Admin', { role: 'Superman' });

    expect(destino(ejecutar(roleGuard('Admin', 'Oficina')))).toBe('/login');
  });

  it('keeps a user who must change the password on the change page, whatever the role', () => {
    iniciarSesionDePrueba('Admin', { dcp: '1' });

    expect(destino(ejecutar(roleGuard('Admin')))).toBe('/cambiar-password');
  });

  it('the change password page sends someone who is not logged in to the login page', () => {
    expect(destino(ejecutar(cambioPasswordGuard))).toBe('/login');
  });

  it('the change password page is open to anyone logged in, whatever the role', () => {
    iniciarSesionDePrueba('Recepcion', { dcp: '1' });

    expect(ejecutar(cambioPasswordGuard)).toBe(true);
  });
});
