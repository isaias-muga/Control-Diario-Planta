import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Observable } from 'rxjs';
import { PASSWORD_MAX_LENGTH, PASSWORD_MIN_LENGTH, PIN_LENGTH } from '../../models/limits';
import { getApiErrorMessage } from '../../utils/api-error';

export interface ValorSecretoDialogData {
  titulo: string;
  etiqueta: string;
  tipo: 'password' | 'pin';
  ayuda?: string;
  /** Sends the value; the dialog closes with `true` when it works and shows the error of the API when it does not. */
  guardar: (valor: string) => Observable<unknown>;
}

/** Asks for a secret (a temporary password, a new PIN) and sends it. The value is never kept after the dialog closes. */
@Component({
  selector: 'app-valor-secreto-dialog',
  imports: [ReactiveFormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule],
  templateUrl: './valor-secreto-dialog.html',
  styleUrl: './valor-secreto-dialog.css',
})
export class ValorSecretoDialog {
  protected data = inject<ValorSecretoDialogData>(MAT_DIALOG_DATA);
  private fb = inject(FormBuilder);
  private dialogRef = inject<MatDialogRef<ValorSecretoDialog, boolean>>(MatDialogRef);

  protected readonly esPin = this.data.tipo === 'pin';
  protected readonly mensajeValidacion = this.esPin ? `El PIN tiene ${PIN_LENGTH} dígitos.` : `Al menos ${PASSWORD_MIN_LENGTH} caracteres.`;

  form = this.fb.nonNullable.group({
    valor: [
      '',
      this.esPin
        ? [Validators.required, Validators.pattern(new RegExp(`^\\d{${PIN_LENGTH}}$`))]
        : [Validators.required, Validators.minLength(PASSWORD_MIN_LENGTH), Validators.maxLength(PASSWORD_MAX_LENGTH)],
    ],
  });

  saving = signal(false);
  saveError = signal<string | null>(null);

  guardar(): void {
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

    this.data.guardar(this.form.getRawValue().valor).subscribe({
      next: () => {
        this.saving.set(false);
        this.dialogRef.close(true);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.saveError.set(getApiErrorMessage(err, 'No se pudo guardar.'));
      },
    });
  }
}
