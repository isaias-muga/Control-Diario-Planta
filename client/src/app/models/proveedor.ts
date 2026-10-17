export interface Proveedor {
  id: string;
  nombre: string;
  cantidadRecepciones: number;
}

/** What the reception phone needs to choose a proveedor: no counts. */
export interface ProveedorOpcion {
  id: string;
  nombre: string;
}

/** The answer to registering a recepción: what was saved and who identified as the operario, if anyone. */
export interface RecepcionRegistrada {
  id: string;
  ticket: number | null;
  litros: number;
  proveedorNombre: string;
  operarioNombre: string | null;
}

export interface RegistrarRecepcion {
  fechaHora: string;
  litros: number;
  grasa: number | null;
  acidez: number;
  temperatura: number;
  silo: number | null;
  observaciones: string | null;
  aguaPorcentaje: number | null;
  ticket: number | null;
  /** The operario PIN: required for the reception phone, ignored for other roles. */
  pin?: string | null;
}
