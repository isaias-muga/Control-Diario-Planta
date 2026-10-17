export type Rol = 'Admin' | 'Oficina' | 'Recepcion';

const ROLES: readonly string[] = ['Admin', 'Oficina', 'Recepcion'];

/** What the client reads from its own token to decide what to show. The server decides what is allowed; this never grants anything. */
export interface SesionToken {
  id: string;
  rol: Rol | null;
  nombre: string;
  /** Seconds since the epoch. */
  emitido: number;
  /** Seconds since the epoch. */
  expira: number;
  /** The token was issued while the user still had to change the password (a hint; the server enforces it). */
  debeCambiarPassword: boolean;
}

/** Reads the payload of a JWT (without checking the signature: only the server can). Null when it is not a token. */
export function leerToken(token: string | null): SesionToken | null {
  if (!token) {
    return null;
  }
  const partes = token.split('.');
  if (partes.length !== 3) {
    return null;
  }
  try {
    const base64 = partes[1].replace(/-/g, '+').replace(/_/g, '/');
    const bytes = Uint8Array.from(atob(base64.padEnd(Math.ceil(base64.length / 4) * 4, '=')), (c) => c.charCodeAt(0));
    const carga = JSON.parse(new TextDecoder().decode(bytes)) as Record<string, unknown>;
    if (typeof carga['exp'] !== 'number') {
      return null;
    }
    const rol = typeof carga['role'] === 'string' && ROLES.includes(carga['role']) ? (carga['role'] as Rol) : null;
    return {
      id: typeof carga['sub'] === 'string' ? carga['sub'] : '',
      rol,
      nombre: typeof carga['name'] === 'string' ? carga['name'] : '',
      emitido: typeof carga['iat'] === 'number' ? carga['iat'] : carga['exp'] - 12 * 3600,
      expira: carga['exp'],
      debeCambiarPassword: carga['dcp'] === '1',
    };
  } catch {
    return null;
  }
}
