import { Rol } from '../utils/jwt';

const TOKEN_KEY = 'dairycontrol_token';

/** A token with the claims the client reads (the signature is not checked on the client). It lasts 12 hours from now. */
export function tokenDePrueba(rol: Rol = 'Admin', extra: Record<string, unknown> = {}): string {
  const emitido = Math.floor(Date.now() / 1000);
  const carga = { sub: 'usuario-de-prueba', role: rol, name: 'Persona de prueba', iat: emitido, exp: emitido + 12 * 3600, ...extra };
  const bytes = new TextEncoder().encode(JSON.stringify(carga));
  const base64 = btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `eyJhbGciOiJIUzI1NiJ9.${base64}.firma`;
}

/** Logs a user in before the services are created (AuthService reads the stored token when it is first injected). */
export function iniciarSesionDePrueba(rol: Rol = 'Admin', extra: Record<string, unknown> = {}): void {
  localStorage.setItem(TOKEN_KEY, tokenDePrueba(rol, extra));
}

export function cerrarSesionDePrueba(): void {
  localStorage.removeItem(TOKEN_KEY);
}
