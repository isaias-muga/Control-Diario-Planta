import { formatearNumeroEs } from './numero-es';
import { formatearFecha } from './periodos';

/** The label of a field of the history (the keys are the ones the API writes). */
const ETIQUETAS: Record<string, string> = {
  proveedor: 'Proveedor',
  litros: 'Litros',
  grasa: 'Grasa (%)',
  acidez: 'Acidez (°D)',
  temperatura: 'Temperatura (°C)',
  agua: 'Agua (%)',
  silo: 'Silo',
  ticket: 'Ticket',
  fechaHora: 'Fecha y hora',
  observaciones: 'Observaciones',
  anulacion: 'Estado',
  nombre: 'Nombre',
  activo: 'Estado',
  alta: 'Alta',
  rol: 'Rol',
  password: 'Contraseña',
  pin: 'PIN',
};

/** The roles as the API writes them in the history of a user, in the words of the Usuarios page. */
const ROLES: Record<string, string> = {
  Admin: 'Administrador',
  Oficina: 'Oficina',
  Consulta: 'Consulta (solo lectura)',
  Recepcion: 'Celular de recepción',
};

const NUMERICOS = new Set(['litros', 'grasa', 'acidez', 'temperatura', 'agua']);
const ENTEROS = new Set(['silo', 'ticket']);

export function etiquetaCampo(campo: string): string {
  return ETIQUETAS[campo] ?? campo;
}

/** YYYY-MM-DDTHH:mm[:ss] to "dd/MM/yyyy HH:mm". */
export function textoFechaHora(fechaHora: string): string {
  return `${formatearFecha(fechaHora.slice(0, 10))} ${fechaHora.slice(11, 16)}`;
}

/** A value of the history as people read it: es-AR numbers, dd/MM/yyyy dates, "—" when it had no value. */
export function textoValor(campo: string, valor: string | null): string {
  if (valor == null || valor === '') {
    return '—';
  }
  if (NUMERICOS.has(campo) || ENTEROS.has(campo)) {
    const numero = Number(valor);
    return Number.isFinite(numero) ? formatearNumeroEs(numero, { entero: ENTEROS.has(campo) }) : valor;
  }
  if (campo === 'fechaHora') {
    return textoFechaHora(valor);
  }
  if (campo === 'rol') {
    return ROLES[valor] ?? valor;
  }
  return valor;
}

/** The UTC instant of the history (ISO, no offset) as plant time: "dd/MM/yyyy HH:mm". */
export function textoInstante(enUtc: string): string {
  const fecha = new Date(enUtc.endsWith('Z') ? enUtc : `${enUtc}Z`);
  const partes = new Intl.DateTimeFormat('es-AR', {
    timeZone: 'America/Argentina/Buenos_Aires',
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }).formatToParts(fecha);
  const valor = (tipo: string) => partes.find((p) => p.type === tipo)?.value ?? '';
  return `${valor('day')}/${valor('month')}/${valor('year')} ${valor('hour')}:${valor('minute')}`;
}
