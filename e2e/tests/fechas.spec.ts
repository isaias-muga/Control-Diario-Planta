import { expect, test } from '../fixtures';
import { abrirRegistro, crearProveedor, descartarRegistro, loginYEsperarResumen, rellenar } from '../helpers';

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

  test('la fecha por defecto del formulario es la hora de la planta', async ({ page }) => {
    await loginYEsperarResumen(page);
    await crearProveedor(page);

    const dialog = await abrirRegistro(page);
    const valor = await dialog.getByLabel('Fecha y hora').inputValue();

    // New York is 1 or 2 hours ahead of Buenos Aires; the default must still be the plant's time
    expect(Math.abs(minutos(valor) - minutos(ahoraEnPlanta()))).toBeLessThanOrEqual(2);
  });
});

test.describe('fechas', () => {
  test.beforeEach(async ({ page }) => {
    await loginYEsperarResumen(page);
  });

  test('una fecha anterior al año 2000 se rechaza junto al campo y no se guarda', async ({ page }) => {
    await crearProveedor(page);
    const dialog = await abrirRegistro(page);

    await rellenar(dialog.getByLabel('Fecha y hora'), '1999-12-31T23:59');
    await rellenar(dialog.getByLabel(/^Litros/), '1000');
    await rellenar(dialog.getByLabel(/^Acidez/), '16');
    await rellenar(dialog.getByLabel(/^Temperatura/), '5');
    await dialog.getByRole('button', { name: 'Registrar', exact: true }).click();

    await expect(dialog.getByText('No puede ser anterior al 01/01/2000.')).toBeVisible();
    await descartarRegistro(page);
    await expect(page.getByText('No hay recepciones con estos filtros.')).toBeVisible();
  });

  test('la API responde 400 en español, sin "(Parameter", si falta la fecha o es inválida', async ({ page }) => {
    await crearProveedor(page);
    const id = page.url().split('/').pop();
    const token = await page.evaluate(() => localStorage.getItem('dairycontrol_token'));
    const headers = { Authorization: `Bearer ${token}` };
    const base = { litros: 1000, acidez: 16, temperatura: 5 };

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
