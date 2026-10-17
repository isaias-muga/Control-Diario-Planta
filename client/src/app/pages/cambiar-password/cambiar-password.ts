import { Component, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { PASSWORD_MAX_LENGTH, PASSWORD_MIN_LENGTH } from '../../models/limits';
import { AuthService } from '../../services/auth';
import { getApiErrorField, getApiErrorMessage } from '../../utils/api-error';

function coinciden(grupo: AbstractControl): ValidationErrors | null {
  return grupo.get('passwordNueva')?.value === grupo.get('repetir')?.value ? null : { noCoinciden: true };
}

/** The user chooses a password of their own (after being created or reset by an admin). Nothing else works until then. */
@Component({
  selector: 'app-cambiar-password',
  imports: [ReactiveFormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule],
  templateUrl: './cambiar-password.html',
  styleUrl: './cambiar-password.css',
})
export class CambiarPassword {
  private auth = inject(AuthService);
  private router = inject(Router);
  private fb = inject(FormBuilder);

  protected readonly minimo = PASSWORD_MIN_LENGTH;
  protected readonly obligatorio = this.auth.debeCambiarPassword;

  form = this.fb.nonNullable.group(
    {
      passwordActual: ['', Validators.required],
      passwordNueva: ['', [Validators.required, Validators.minLength(PASSWORD_MIN_LENGTH), Validators.maxLength(PASSWORD_MAX_LENGTH)]],
      repetir: ['', Validators.required],
    },
    { validators: coinciden },
  );

  guardando = signal(false);
  error = signal<string | null>(null);
  errorActual = signal<string | null>(null);
  errorNueva = signal<string | null>(null);

  submit(): void {
    // A second tap can arrive before the disabled button is rendered
    if (this.guardando()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { passwordActual, passwordNueva } = this.form.getRawValue();
    this.guardando.set(true);
    this.error.set(null);
    this.errorActual.set(null);
    this.errorNueva.set(null);

    this.auth.cambiarPassword(passwordActual, passwordNueva).subscribe({
      next: () => this.router.navigateByUrl(this.auth.inicio()),
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        const mensaje = getApiErrorMessage(err, 'No se pudo cambiar la contraseña.');
        const campo = getApiErrorField(err);
        if (campo === 'passwordActual') {
          this.errorActual.set(mensaje);
        } else if (campo === 'passwordNueva') {
          this.errorNueva.set(mensaje);
        } else {
          this.error.set(mensaje);
        }
      },
    });
  }

  salir(): void {
    this.auth.logout();
  }
}
