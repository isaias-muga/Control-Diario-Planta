import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Observable } from 'rxjs';
import { MOTIVO_MAX_LENGTH, MOTIVO_MIN_LENGTH, NOMBRE_MAX_LENGTH } from '../../models/limits';
import { getApiErrorField, getApiErrorMessage } from '../../utils/api-error';
import { motivoSuficiente } from '../../utils/motivo';

export interface MotivoDialogData {
  titulo: string;
  mensaje?: string;
  confirmar: string;
  /** The action is hard to undo (void, deactivate): the confirm button is red. */
  peligro?: boolean;
  /** Also asks for a name (correcting the name of a proveedor). */
  nombre?: { etiqueta: string; valor: string };
  /** Does the change; the dialog stays open and shows the API's message when it fails. */
  guardar: (motivo: string, nombre: string | null) => Observable<unknown>;
}

/**
 * Asks for the reason of a change that has to leave a history (void a recepción, correct the name of a proveedor, deactivate or
 * reactivate a proveedor) and does it. It closes with `true` when the change was saved.
 */
@Component({
  selector: 'app-motivo-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  templateUrl: './motivo-dialog.html',
  styleUrl: './motivo-dialog.css',
})
export class MotivoDialog {
  protected data = inject<MotivoDialogData>(MAT_DIALOG_DATA);
  private dialogRef = inject<MatDialogRef<MotivoDialog, boolean>>(MatDialogRef);
  private fb = inject(FormBuilder).nonNullable;

  protected readonly motivoMin = MOTIVO_MIN_LENGTH;
  protected readonly motivoMax = MOTIVO_MAX_LENGTH;
  protected readonly nombreMax = NOMBRE_MAX_LENGTH;

  protected form = this.fb.group({
    nombre: [
      this.data.nombre?.valor ?? '',
      this.data.nombre ? [Validators.required, Validators.pattern(/\S/), Validators.maxLength(NOMBRE_MAX_LENGTH)] : [],
    ],
    motivo: ['', [motivoSuficiente, Validators.maxLength(MOTIVO_MAX_LENGTH)]],
  });
  protected saving = signal(false);
  protected saveError = signal<string | null>(null);

  protected confirmar(): void {
    // A second click can arrive before the disabled button is rendered
    if (this.saving()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.saveError.set(null);
    const { nombre, motivo } = this.form.getRawValue();
    this.data.guardar(motivo.trim(), this.data.nombre ? nombre.trim() : null).subscribe({
      next: () => {
        this.saving.set(false);
        this.dialogRef.close(true);
      },
      error: (err: HttpErrorResponse) => {
        if (getApiErrorField(err) === 'motivo') {
          this.form.controls.motivo.setErrors({ motivoCorto: true });
          this.form.controls.motivo.markAsTouched();
        }
        this.saveError.set(getApiErrorMessage(err, 'No se pudo guardar el cambio.'));
        this.saving.set(false);
      },
    });
  }
}
