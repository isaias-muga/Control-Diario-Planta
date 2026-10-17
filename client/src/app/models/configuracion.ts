export interface LimitesCalidad {
  acidezMaxima: number;
  temperaturaMaxima: number;
  aguaMaxima: number;
  /** How many silos the plant has: a recepción names one of 1..cantidadSilos. */
  cantidadSilos: number;
  actualizadoEn: string;
  /** Opaque version of these limits: the update sends it back so the API can reject a stale edit (409). */
  version: string;
}

export interface ActualizarLimitesCalidad {
  acidezMaxima: number;
  temperaturaMaxima: number;
  aguaMaxima: number;
  cantidadSilos: number;
  version: string;
}
