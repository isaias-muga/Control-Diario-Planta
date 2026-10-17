import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import {
  NOMBRE_PERSONA_MAX_LENGTH,
  PASSWORD_MAX_LENGTH,
  PASSWORD_MIN_LENGTH,
  USERNAME_MAX_LENGTH,
  USERNAME_MIN_LENGTH,
} from '../../models/limits';
import { Usuario } from '../../models/usuarios';
import { UsuariosService } from '../../services/usuarios';
import { getApiErrorField, getApiErrorMessage } from '../../utils/api-error';
import { Rol } from '../../utils/jwt';

export const ETIQUETAS_ROL: Record<Rol, string> = {
  Admin: 'Administrador',
  Oficina: 'Oficina (solo lectura)',
  Recepcion: 'Celular de recepción',
};

/** A new user with a temporary password: the person has to choose their own at the first login. */
@Component({
  selector: 'app-usuario-dialog',
  imports: [ReactiveFormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  templateUrl: './usuario-dialog.html',
  styleUrl: './usuario-dialog.css',
})
export class UsuarioDialog {
  private usuarios = inject(UsuariosService);
  private fb = inject(FormBuilder);
  private dialogRef = inject<MatDialogRef<UsuarioDialog, Usuario>>(MatDialogRef);

  protected readonly roles = Object.keys(ETIQUETAS_ROL) as Rol[];
  protected readonly etiquetas = ETIQUETAS_ROL;
  protected readonly usernameMin = USERNAME_MIN_LENGTH;
  protected readonly usernameMax = USERNAME_MAX_LENGTH;
  protected readonly nombreMax = NOMBRE_PERSONA_MAX_LENGTH;
  protected readonly passwordMin = PASSWORD_MIN_LENGTH;

  form = this.fb.nonNullable.group({
    username: [
      '',
      [Validators.required, Validators.minLength(USERNAME_MIN_LENGTH), Validators.maxLength(USERNAME_MAX_LENGTH), Validators.pattern(/^\s*[A-Za-z0-9._-]+\s*$/)],
    ],
    nombre: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(NOMBRE_PERSONA_MAX_LENGTH)]],
    rol: ['Oficina' as Rol, Validators.required],
    password: ['', [Validators.required, Validators.minLength(PASSWORD_MIN_LENGTH), Validators.maxLength(PASSWORD_MAX_LENGTH)]],
  });

  saving = signal(false);
  saveError = signal<string | null>(null);
  errorCampo = signal<{ campo: string; mensaje: string } | null>(null);

  protected errorDe(campo: string): string | null {
    const e = this.errorCampo();
    return e?.campo === campo ? e.mensaje : null;
  }

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
    this.errorCampo.set(null);

    const { username, nombre, rol, password } = this.form.getRawValue();
    this.usuarios.crearUsuario({ username: username.trim(), nombre: nombre.trim(), rol, password }).subscribe({
      next: (creado) => {
        this.saving.set(false);
        this.dialogRef.close(creado);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        const mensaje = getApiErrorMessage(err, 'No se pudo crear el usuario.');
        const campo = getApiErrorField(err);
        if (campo && ['username', 'nombre', 'rol', 'password'].includes(campo)) {
          this.errorCampo.set({ campo, mensaje });
        } else {
          this.saveError.set(mensaje);
        }
      },
    });
  }
}
