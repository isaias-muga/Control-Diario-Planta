import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { UsuariosService } from './usuarios';

describe('UsuariosService', () => {
  let service: UsuariosService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(UsuariosService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('desactivarUsuario and reactivarUsuario send the reason with POST', () => {
    service.desactivarUsuario('u1', 'Dejó la empresa').subscribe();
    const desactivar = http.expectOne((r) => r.url.endsWith('/Usuarios/u1/desactivar'));
    expect(desactivar.request.method).toBe('POST');
    expect(desactivar.request.body).toEqual({ motivo: 'Dejó la empresa' });
    desactivar.flush(null, { status: 204, statusText: 'No Content' });

    service.reactivarUsuario('u1', 'Volvió a trabajar').subscribe();
    const reactivar = http.expectOne((r) => r.url.endsWith('/Usuarios/u1/reactivar'));
    expect(reactivar.request.method).toBe('POST');
    expect(reactivar.request.body).toEqual({ motivo: 'Volvió a trabajar' });
    reactivar.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('cambiarRol sends the role and the reason with PUT', () => {
    service.cambiarRol('u1', 'Consulta', 'Pasa a solo lectura').subscribe();
    const req = http.expectOne((r) => r.url.endsWith('/Usuarios/u1/rol'));

    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ rol: 'Consulta', motivo: 'Pasa a solo lectura' });
    req.flush({});
  });

  it('desactivarOperario and reactivarOperario send the reason with POST', () => {
    service.desactivarOperario('o1', 'Dejó de trabajar').subscribe();
    const desactivar = http.expectOne((r) => r.url.endsWith('/Operarios/o1/desactivar'));
    expect(desactivar.request.method).toBe('POST');
    expect(desactivar.request.body).toEqual({ motivo: 'Dejó de trabajar' });
    desactivar.flush(null, { status: 204, statusText: 'No Content' });

    service.reactivarOperario('o1', 'Volvió a trabajar').subscribe();
    const reactivar = http.expectOne((r) => r.url.endsWith('/Operarios/o1/reactivar'));
    expect(reactivar.request.method).toBe('POST');
    expect(reactivar.request.body).toEqual({ motivo: 'Volvió a trabajar' });
    reactivar.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('reads the history of a user and of an operario with GET', () => {
    service.getHistorialUsuario('u1').subscribe();
    const deUsuario = http.expectOne((r) => r.url.endsWith('/Usuarios/u1/historial'));
    expect(deUsuario.request.method).toBe('GET');
    deUsuario.flush([]);

    service.getHistorialOperario('o1').subscribe();
    const deOperario = http.expectOne((r) => r.url.endsWith('/Operarios/o1/historial'));
    expect(deOperario.request.method).toBe('GET');
    deOperario.flush([]);
  });
});
