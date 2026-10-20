import { RecepcionDetalle } from '../models/correcciones';
import { textoValor } from './historial';

/** What the correction form holds for each field (numbers already parsed). */
export interface ValoresCorreccion {
  proveedorId: string;
  litros: number | null;
  grasa: number | null;
  acidez: number | null;
  temperatura: number | null;
  aguaPorcentaje: number | null;
  silo: number | null;
  ticket: number | null;
  /** YYYY-MM-DDTHH:mm */
  fechaHora: string;
  observaciones: string | null;
}

export interface CambioMostrado {
  campo: string;
  anterior: string;
  nuevo: string;
}

const numero = (valor: number | null | undefined): string | null => (valor == null ? null : String(valor));
const texto = (valor: string | null | undefined): string | null => {
  const t = valor?.trim();
  return t ? t : null;
};

/**
 * The fields that differ between the recepción as it is and the form, as "old to new" in the form people read, in the order of the
 * form. It only shows; the server decides what changed and records the history.
 */
export function cambiosDeRecepcion(original: RecepcionDetalle, nuevos: ValoresCorreccion, nombreProveedorNuevo: string): CambioMostrado[] {
  const cambios: CambioMostrado[] = [];
  const anotar = (campo: string, anterior: string | null, nuevo: string | null, nombres?: [string, string]) => {
    if (anterior !== nuevo) {
      cambios.push({
        campo,
        anterior: nombres ? nombres[0] : textoValor(campo, anterior),
        nuevo: nombres ? nombres[1] : textoValor(campo, nuevo),
      });
    }
  };

  if (original.proveedorId !== nuevos.proveedorId) {
    anotar('proveedor', original.proveedorId, nuevos.proveedorId, [original.proveedorNombre, nombreProveedorNuevo]);
  }
  anotar('fechaHora', original.fechaHora.slice(0, 16), nuevos.fechaHora.slice(0, 16));
  anotar('ticket', numero(original.ticket), numero(nuevos.ticket));
  anotar('litros', numero(original.litros), numero(nuevos.litros));
  anotar('grasa', numero(original.grasa), numero(nuevos.grasa));
  anotar('acidez', numero(original.acidez), numero(nuevos.acidez));
  anotar('temperatura', numero(original.temperatura), numero(nuevos.temperatura));
  anotar('agua', numero(original.aguaPorcentaje), numero(nuevos.aguaPorcentaje));
  anotar('silo', numero(original.silo), numero(nuevos.silo));
  anotar('observaciones', texto(original.observaciones), texto(nuevos.observaciones));
  return cambios;
}
