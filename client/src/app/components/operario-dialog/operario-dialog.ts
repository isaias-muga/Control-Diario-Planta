import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { NOMBRE_PERSONA_MAX_LENGTH, PIN_LENGTH } from '../../models/limits';
import { Operario } from '../../models/usuarios';
import { UsuariosService } from '../../services/usuarios';
import { getApiErrorField, getApiErrorMessage } from '../../utils/api-error';

/** A new operario of the reception phone, with the PIN the admin gives them. */
@Component({
  selector: 'app-operario-dialog',
  imports: [ReactiveFormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule],
  templateUrl: './operario-dialog.html',
  styleUrl: './operario-dialog.css',
})
export class OperarioDialog {
  private usuarios = inject(UsuariosService);
  private fb = inject(FormBuilder);
  private dialogRef = inject<MatDialogRef<OperarioDialog, Operario>>(MatDialogRef);

  protected readonly nombreMax = NOMBRE_PERSONA_MAX_LENGTH;
  protected readonly pinLength = PIN_LENGTH;

  form = this.fb.nonNullable.group({
    nombre: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(NOMBRE_PERSONA_MAX_LENGTH)]],
    pin: ['', [Validators.required, Validators.pattern(new RegExp(`^\\d{${PIN_LENGTH}}$`))]],
  });

  saving = signal(false);
  saveError = signal<string | null>(null);
  errorPin = signal<string | null>(null);

  crear(): void {
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
    this.errorPin.set(null);

    const { nombre, pin } = this.form.getRawValue();
    this.usuarios.crearOperario({ nombre: nombre.trim(), pin }).subscribe({
      next: (creado) => {
        this.saving.set(false);
        this.dialogRef.close(creado);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        const mensaje = getApiErrorMessage(err, 'No se pudo crear el operario.');
        if (getApiErrorField(err) === 'pin' || err.status === 409) {
          this.errorPin.set(mensaje);
        } else {
          this.saveError.set(mensaje);
        }
      },
    });
  }
}
