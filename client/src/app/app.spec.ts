import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { App } from './app';
import { cerrarSesionDePrueba, iniciarSesionDePrueba } from './testing/sesion';
import { Rol } from './utils/jwt';

describe('App', () => {
  beforeEach(async () => {
    cerrarSesionDePrueba();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  afterEach(() => cerrarSesionDePrueba());

  async function abrir(rol: Rol | null, extra: Record<string, unknown> = {}): Promise<HTMLElement> {
    if (rol) {
      iniciarSesionDePrueba(rol, extra);
    }
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render title', async () => {
    const el = await abrir(null);

    expect(el.querySelector('mat-toolbar')?.textContent).toContain('Control Diario Fábrica');
  });

  it.each<Rol>(['Admin', 'Oficina', 'Consulta'])('shows the three section tabs, the settings button and logout to %s', async (rol) => {
    const el = await abrir(rol);

    const enlaces = Array.from(el.querySelectorAll('nav a')).map((a) => a.textContent?.trim());
    expect(enlaces).toEqual(['Resumen', 'Recepciones', 'Proveedores']);
    expect(el.querySelector('button[aria-label="Ajustes"]')).not.toBeNull();
    expect(el.querySelector('button[aria-label="Ajustes"]')?.closest('nav')).toBeNull();
    expect(Array.from(el.querySelectorAll('button')).map((b) => b.textContent?.trim())).toContain('Cerrar sesión');
  });

  it('shows no toolbar, tabs, settings or logout to the reception phone', async () => {
    const el = await abrir('Recepcion');

    expect(el.querySelector('mat-toolbar')).toBeNull();
    expect(el.querySelector('nav')).toBeNull();
    expect(el.querySelector('button[aria-label="Ajustes"]')).toBeNull();
    expect(el.textContent).not.toContain('Cerrar sesión');
  });

  it('shows only the title and logout to a user who must change the password', async () => {
    const el = await abrir('Oficina', { dcp: '1' });

    expect(el.querySelector('nav')).toBeNull();
    expect(el.querySelector('button[aria-label="Ajustes"]')).toBeNull();
    expect(el.textContent).toContain('Cerrar sesión');
  });

  it('offers the login link when nobody is logged in', async () => {
    const el = await abrir(null);

    expect(el.querySelector('nav')).toBeNull();
    expect(el.textContent).toContain('Iniciar sesión');
  });
});
