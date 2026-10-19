import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ProveedoresList } from './proveedores-list';
import { cerrarSesionDePrueba, iniciarSesionDePrueba } from '../../testing/sesion';
import { Rol } from '../../utils/jwt';

describe('ProveedoresList', () => {
  afterEach(() => cerrarSesionDePrueba());

  async function abrir(rol: Rol): Promise<HTMLElement> {
    iniciarSesionDePrueba(rol);
    TestBed.configureTestingModule({
      imports: [ProveedoresList],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ProveedoresList);
    await fixture.whenStable();
    http.expectOne((r) => r.url.endsWith('/Proveedores')).flush([{ id: 'p1', nombre: 'Tambo Don Pedro', cantidadRecepciones: 3 }]);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement;
  }

  const botones = (el: HTMLElement): string[] => Array.from(el.querySelectorAll('button')).map((b) => b.textContent?.trim() ?? '');

  it('lists the proveedores with their counts', async () => {
    const el = await abrir('Admin');

    expect(el.textContent).toContain('Tambo Don Pedro');
    expect(el.textContent).toContain('3 recepciones');
  });

  it('offers "Nuevo proveedor" to an admin', async () => {
    expect(botones(await abrir('Admin'))).toContain('Nuevo proveedor');
  });

  it('offers no way to create a proveedor to Consulta (read only)', async () => {
    const el = await abrir('Consulta');

    expect(botones(el)).not.toContain('Nuevo proveedor');
    expect(el.textContent).toContain('Tambo Don Pedro');
  });

  it('offers no way to create a proveedor to the office (read only)', async () => {
    const el = await abrir('Oficina');

    expect(botones(el)).not.toContain('Nuevo proveedor');
    expect(el.textContent).toContain('Tambo Don Pedro');
  });
});
