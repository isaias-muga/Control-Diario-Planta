import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ProveedoresService } from './proveedores';

describe('Proveedores', () => {
  let service: ProveedoresService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ProveedoresService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('renombrar sends the name, the reason and the version with PUT', () => {
    service.renombrar('abc', 'Tambo S.A.', 'Mal escrito', 'AAE=').subscribe();
    const req = http.expectOne((r) => r.url.endsWith('/Proveedores/abc'));

    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ nombre: 'Tambo S.A.', motivo: 'Mal escrito', version: 'AAE=' });
    req.flush({});
  });

  it('desactivar and reactivar send the reason with POST', () => {
    service.desactivar('abc', 'Cerró el tambo').subscribe();
    const desactivar = http.expectOne((r) => r.url.endsWith('/Proveedores/abc/desactivar'));
    expect(desactivar.request.method).toBe('POST');
    expect(desactivar.request.body).toEqual({ motivo: 'Cerró el tambo' });
    desactivar.flush(null, { status: 204, statusText: 'No Content' });

    service.reactivar('abc', 'Volvió a entregar').subscribe();
    const reactivar = http.expectOne((r) => r.url.endsWith('/Proveedores/abc/reactivar'));
    expect(reactivar.request.method).toBe('POST');
    expect(reactivar.request.body).toEqual({ motivo: 'Volvió a entregar' });
    reactivar.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('getHistorial reads the history of the proveedor', () => {
    let resultado: unknown;

    service.getHistorial('abc').subscribe((h) => (resultado = h));
    http.expectOne((r) => r.url.endsWith('/Proveedores/abc/historial')).flush([]);

    expect(resultado).toEqual([]);
  });

  it('there is no way to delete a proveedor: the service has no such method', () => {
    expect(Object.getOwnPropertyNames(Object.getPrototypeOf(service)).filter((n) => /borrar|eliminar|delete|remove/i.test(n))).toEqual([]);
  });

  it('getById asks for one proveedor and gets its name and count', () => {
    let resultado: unknown;

    service.getById('abc').subscribe((p) => (resultado = p));
    http.expectOne((r) => r.url.endsWith('/Proveedores/abc')).flush({ id: 'abc', nombre: 'Tambo', cantidadRecepciones: 4 });

    expect(resultado).toEqual({ id: 'abc', nombre: 'Tambo', cantidadRecepciones: 4 });
  });
});
