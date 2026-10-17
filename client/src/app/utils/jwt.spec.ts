import { leerToken } from './jwt';

function token(carga: Record<string, unknown>): string {
  const bytes = new TextEncoder().encode(JSON.stringify(carga));
  const base64 = btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `eyJhbGciOiJIUzI1NiJ9.${base64}.firma`;
}

describe('leerToken', () => {
  it('reads the id, role, name and the dates', () => {
    const sesion = leerToken(token({ sub: 'u1', role: 'Recepcion', name: 'María Núñez', iat: 100, exp: 700 }));

    expect(sesion).toEqual({ id: 'u1', rol: 'Recepcion', nombre: 'María Núñez', emitido: 100, expira: 700, debeCambiarPassword: false });
  });

  it('reads the must-change-password hint', () => {
    expect(leerToken(token({ sub: 'u1', role: 'Admin', exp: 700, dcp: '1' }))?.debeCambiarPassword).toBe(true);
  });

  it('ignores a role it does not know', () => {
    expect(leerToken(token({ sub: 'u1', role: 'Superman', exp: 700 }))?.rol).toBeNull();
  });

  it.each([null, '', 'abc', 'a.b', 'a.!!!.c', `x.${btoa('no json')}.y`, token({ sub: 'u1' })])('returns null for %s', (valor) => {
    expect(leerToken(valor)).toBeNull();
  });
});
