import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { cerrarSesionDePrueba, iniciarSesionDePrueba } from '../../testing/sesion';
import { ProveedoresList } from './proveedores-list';

describe('ProveedoresList: inactive proveedores', () => {
  afterEach(() => cerrarSesionDePrueba());

  it('keeps an inactive proveedor in the list, marked as inactive, with its recepciones count', async () => {
    iniciarSesionDePrueba('Oficina');
    TestBed.configureTestingModule({
      imports: [ProveedoresList],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ProveedoresList);
    await fixture.whenStable();
    http.expectOne((r) => r.url.endsWith('/Proveedores')).flush([
      { id: 'p1', nombre: 'Tambo Activo', cantidadRecepciones: 3, activo: true },
      { id: 'p2', nombre: 'Tambo Cerrado', cantidadRecepciones: 9, activo: false },
    ]);
    await fixture.whenStable();
    fixture.detectChanges();

    const filas = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('a[mat-list-item]')).map((a) => (a.textContent ?? '').replace(/\s+/g, ' ').trim());
    expect(filas).toHaveLength(2);
    expect(filas[0]).toBe('Tambo Activo (3 recepciones)');
    expect(filas[1]).toBe('Tambo Cerrado (9 recepciones) · inactivo');
  });
});
