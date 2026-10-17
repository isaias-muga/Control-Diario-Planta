export type MotivoFueraDeRango = 'Acidez' | 'Temperatura' | 'Agua';

export interface RecepcionListado {
  id: string;
  ticket: number | null;
  fechaHora: string;
  proveedorId: string;
  proveedorNombre: string;
  litros: number;
  aguaPorcentaje: number | null;
  litrosReales: number;
  grasa: number | null;
  acidez: number;
  temperatura: number;
  silo: number | null;
  observaciones: string | null;
  /** Who loaded it (the operario when a PIN identified one, otherwise the user); null for old rows. */
  registradoPor: string | null;
  motivos: MotivoFueraDeRango[];
}

export interface TotalesListado {
  litros: number;
  litrosReales: number;
  cantidadRecepciones: number;
  ticketMinimo: number | null;
  ticketMaximo: number | null;
  cantidadFueraDeRango: number;
  fueraPorAcidez: number;
  fueraPorTemperatura: number;
  fueraPorAgua: number;
}

export interface SubtotalDia {
  fecha: string; // YYYY-MM-DD
  cantidadRecepciones: number;
  litros: number;
  cantidadFueraDeRango: number;
}

export interface ListadoRecepciones {
  items: RecepcionListado[];
  totales: TotalesListado;
  dias: SubtotalDia[];
  pagina: number;
  tamanoPagina: number;
  hayMas: boolean;
}

export interface FiltroRecepciones {
  desde: string;
  hasta: string;
  proveedorId?: string | null;
  ticket?: number | null;
  soloFueraDeRango?: boolean;
  pagina?: number;
}

export interface SiguienteTicket {
  /** Null when there is nothing to suggest. */
  ticket: number | null;
}
