import { expect, test } from '../fixtures';
import { abrirRegistro, crearProveedor, llenarRecepcion, loginYEsperarResumen, revisarDatos } from '../helpers';

const ZONA_PLANTA = 'America/Argentina/Buenos_Aires';

/** The plant's wall-clock time as YYYY-MM-DDTHH:mm, computed in the test independently of the app. */
function ahoraEnPlanta(): string {
  const partes = new Intl.DateTimeFormat('en-CA', {
    timeZone: ZONA_PLANTA,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }).formatToParts(new Date());
  const get = (tipo: string) => partes.find((p) => p.type === tipo)!.value;
  return `${get('year')}-${get('month')}-${get('day')}T${get('hour')}:${get('minute')}`;
}

const minutos = (fechaHora: string) => Date.parse(`${fechaHora}:00Z`) / 60_000;

test.describe('fechas con el navegador en otra zona horaria', () => {
  test.use({ timezoneId: 'America/New_York' });

  test('la fecha y hora que se guarda (la de la confirmación) es la hora de la planta', async ({ page }) => {
    await loginYEsperarResumen(page);
    await crearProveedor(page);
    await abrirRegistro(page);
    await llenarRecepcion(page, { litros: 1000, acidez: 16, temperatura: 5 });

    await revisarDatos(page);

    // "Fecha y hora" on the confirmation reads dd/MM/yyyy HH:mm
    const texto = await page.locator('dt', { hasText: 'Fecha y hora' }).locator('xpath=following-sibling::dd[1]').innerText();
    const [dia, hora] = texto.trim().split(' ');
    const [d, m, a] = dia.split('/');
    const mostrado = `${a}-${m}-${d}T${hora}`;
    // New York is 1 or 2 hours ahead of Buenos Aires; the time shown (and sent) must still be the plant's
    expect(Math.abs(minutos(mostrado) - minutos(ahoraEnPlanta()))).toBeLessThanOrEqual(2);
  });
});

test.describe('fechas', () => {
  test.beforeEach(async ({ page }) => {
    await loginYEsperarResumen(page);
  });

  test('la API responde 400 en español, sin "(Parameter", si falta la fecha o es inválida', async ({ page }) => {
    await crearProveedor(page);
    const id = page.url().split('/').pop();
    const token = await page.evaluate(() => localStorage.getItem('dairycontrol_token'));
    const headers = { Authorization: `Bearer ${token}` };
    const base = { litros: 1000, acidez: 16, temperatura: 5, silo: 1 };

    const casos: { data: object; mensaje: RegExp }[] = [
      { data: base, mensaje: /obligatoria/ },
      { data: { ...base, fechaHora: '1999-12-31T23:59:00' }, mensaje: /anterior al 01\/01\/2000/ },
      { data: { ...base, fechaHora: '2099-01-01T00:00:00' }, mensaje: /hora actual/ },
      { data: { ...base, fechaHora: 'ayer' }, mensaje: /no es válido/ },
    ];
    for (const caso of casos) {
      const respuesta = await page.request.post(`/api/Proveedores/${id}/recepciones`, { headers, data: caso.data });
      const cuerpo = await respuesta.json();

      expect(respuesta.status()).toBe(400);
      expect(cuerpo.error).toMatch(caso.mensaje);
      expect(cuerpo.error).not.toContain('(Parameter');
      expect(cuerpo.field).toBe('fechaHora');
    }
  });
});
