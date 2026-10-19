import { AbstractControl, ValidationErrors } from '@angular/forms';
import { MOTIVO_MIN_LENGTH } from '../models/limits';

/** The reason of a change is required: at least MOTIVO_MIN_LENGTH characters once trimmed (the same rule as the server). */
export function motivoSuficiente(control: AbstractControl): ValidationErrors | null {
  const texto = typeof control.value === 'string' ? control.value.trim() : '';
  return texto.length >= MOTIVO_MIN_LENGTH ? null : { motivoCorto: true };
}
