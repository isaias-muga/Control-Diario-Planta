import { AbstractControl, FormBuilder, ValidationErrors, Validators } from '@angular/forms';
import {
  ACIDEZ_MAX,
  ACIDEZ_MIN,
  AGUA_MAX_PORCENTAJE,
  FECHA_ADELANTO_MINUTOS,
  FECHA_MIN,
  GRASA_MAX,
  GRASA_MIN,
  LITROS_MAX,
  LITROS_MIN,
  OBSERVACIONES_MAX_LENGTH,
  SILO_MAX,
  SILO_MIN,
  TEMPERATURA_MAX,
  TEMPERATURA_MIN,
  TICKET_MAX,
  TICKET_MIN,
} from '../../models/limits';
import { ahoraEnPlanta } from '../../utils/periodos';

/** fechaHora must be between FECHA_MIN and the plant's now + FECHA_ADELANTO_MINUTOS (YYYY-MM-DDTHH:mm strings compare in order). */
export function fechaHoraEnRango(control: AbstractControl): ValidationErrors | null {
  const valor = typeof control.value === 'string' ? control.value.slice(0, 16) : '';
  if (!valor) {
    return null; // `required` reports the empty case
  }
  if (valor < FECHA_MIN) {
    return { fechaMin: true };
  }
  if (valor > ahoraEnPlanta(new Date(Date.now() + FECHA_ADELANTO_MINUTOS * 60_000))) {
    return { fechaMax: true };
  }
  return null;
}

/** The fields and plausible-range validators of a recepción, for the registration flow. */
export function crearFormularioRecepcion(fb: FormBuilder) {
  return fb.group({
    proveedorId: ['', Validators.required],
    fechaHora: [ahoraEnPlanta(), [Validators.required, fechaHoraEnRango]],
    litros: [
      null as number | null,
      [Validators.required, Validators.min(LITROS_MIN), Validators.max(LITROS_MAX)],
    ],
    grasa: [null as number | null, [Validators.min(GRASA_MIN), Validators.max(GRASA_MAX)]],
    acidez: [
      null as number | null,
      [Validators.required, Validators.min(ACIDEZ_MIN), Validators.max(ACIDEZ_MAX)],
    ],
    temperatura: [
      null as number | null,
      [Validators.required, Validators.min(TEMPERATURA_MIN), Validators.max(TEMPERATURA_MAX)],
    ],
    // Required: chosen with the toggle buttons 1..N (N is the number of silos of the plant, read from the API)
    silo: [null as number | null, [Validators.required, Validators.min(SILO_MIN), Validators.max(SILO_MAX)]],
    aguaPorcentaje: [
      null as number | null,
      [Validators.min(0), Validators.max(AGUA_MAX_PORCENTAJE)],
    ],
    ticket: [
      null as number | null,
      [Validators.min(TICKET_MIN), Validators.max(TICKET_MAX), Validators.pattern(/^\d+$/)],
    ],
    observaciones: ['', Validators.maxLength(OBSERVACIONES_MAX_LENGTH)],
  });
}
