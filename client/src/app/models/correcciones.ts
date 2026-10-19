/** A recepción as the correction form needs it, with the version that the correction must send back. */
export interface RecepcionDetalle {
  id: string;
  proveedorId: string;
  proveedorNombre: string;
  proveedorActivo: boolean;
  ticket: number | null;
  /** Plant time, YYYY-MM-DDTHH:mm:ss. */
  fechaHora: string;
  litros: number;
  aguaPorcentaje: number | null;
  grasa: number | null;
  acidez: number;
  temperatura: number;
  silo: number | null;
  observaciones: string | null;
  anulada: boolean;
  version: string;
}

/** The fields of registering, plus the proveedor it is for now, the reason and the version that was read. */
export interface CorregirRecepcion {
  proveedorId: string;
  litros: number;
  grasa: number | null;
  acidez: number;
  temperatura: number;
  aguaPorcentaje: number | null;
  silo: number;
  ticket: number | null;
  fechaHora: string;
  observaciones: string | null;
  motivo: string;
  version: string;
}

/** One field of a correction as the history keeps it: invariant text (numbers with a dot), null when it had no value. */
export interface CampoCambiado {
  campo: string;
  valorAnterior: string | null;
  valorNuevo: string | null;
}

/** One correction, void or deactivation: who, when (UTC), why and the fields it changed. */
export interface Correccion {
  correccionId: string;
  enUtc: string;
  usuario: string | null;
  motivo: string;
  cambios: CampoCambiado[];
}
