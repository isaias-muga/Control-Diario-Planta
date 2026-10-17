import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import {
  ACIDEZ_LIMITE_MAX,
  ACIDEZ_LIMITE_MIN,
  AGUA_LIMITE_MAX,
  AGUA_LIMITE_MIN,
  TEMPERATURA_LIMITE_MAX,
  TEMPERATURA_LIMITE_MIN,
} from '../../models/limits';
import { LimitesCalidad } from '../../models/configuracion';
import { AuthService } from '../../services/auth';
import { ConfiguracionService } from '../../services/configuracion';
import { getApiErrorMessage } from '../../utils/api-error';
import { errorNumero } from '../../utils/numero-es';
import { NumeroInput } from '../../components/numero-input/numero-input';

@Component({
  selector: 'app-configuracion',
  imports: [
    ReactiveFormsModule,
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    NumeroInput,
  ],
  templateUrl: './configuracion.html',
  styleUrl: './configuracion.css',
})
export class Configuracion {
  protected auth = inject(AuthService);
  private configuracionService = inject(ConfiguracionService);
  private fb = inject(FormBuilder);
  private snackBar = inject(MatSnackBar);

  protected readonly errorNumero = errorNumero;
  readonly acidezMin = ACIDEZ_LIMITE_MIN;
  readonly acidezMax = ACIDEZ_LIMITE_MAX;
  readonly temperaturaMin = TEMPERATURA_LIMITE_MIN;
  readonly temperaturaMax = TEMPERATURA_LIMITE_MAX;
  readonly aguaMin = AGUA_LIMITE_MIN;
  readonly aguaMax = AGUA_LIMITE_MAX;

  form = this.fb.group({
    acidezMaxima: [
      null as number | null,
      [Validators.required, Validators.min(ACIDEZ_LIMITE_MIN), Validators.max(ACIDEZ_LIMITE_MAX)],
    ],
    temperaturaMaxima: [
      null as number | null,
      [
        Validators.required,
        Validators.min(TEMPERATURA_LIMITE_MIN),
        Validators.max(TEMPERATURA_LIMITE_MAX),
      ],
    ],
    aguaMaxima: [
      null as number | null,
      [Validators.required, Validators.min(AGUA_LIMITE_MIN), Validators.max(AGUA_LIMITE_MAX)],
    ],
  });

  loading = signal(true);
  saving = signal(false);
  error = signal<string | null>(null);
  saveError = signal<string | null>(null);
  actualizadoEn = signal<string | null>(null);
  /** The version of the limits on screen: sent back on save so the API can tell if someone else changed them. */
  private version: string | null = null;

  // The API sends UTC timestamps; make sure the browser reads them as UTC and shows local time.
  ultimaModificacion = computed(() => {
    const valor = this.actualizadoEn();
    if (!valor) return null;
    return /(Z|[+-]\d{2}:\d{2})$/.test(valor) ? valor : `${valor}Z`;
  });

  constructor() {
    // The office reads the limits; only an admin changes them (the API refuses anyone else)
    if (!this.auth.esAdmin()) {
      this.form.disable();
    }
    this.cargar();
  }

  invalid(control: keyof typeof this.form.controls): boolean {
    const c = this.form.controls[control];
    return c.touched && c.invalid;
  }

  cargar(): void {
    this.loading.set(true);
    this.error.set(null);

    this.configuracionService.getCalidad().subscribe({
      next: (limites) => {
        this.aplicar(limites);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.error.set(getApiErrorMessage(err, 'No se pudo cargar la configuración.'));
        this.loading.set(false);
      },
    });
  }

  guardar(): void {
    if (!this.auth.esAdmin()) {
      return;
    }
    // A second click can arrive before the disabled button is rendered; it would send the version that the first save just replaced and be reported as a conflict
    if (this.saving()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const v = this.form.getRawValue();
    this.saving.set(true);
    this.saveError.set(null);

    this.configuracionService
      .actualizarCalidad({
        acidezMaxima: v.acidezMaxima!,
        temperaturaMaxima: v.temperaturaMaxima!,
        aguaMaxima: v.aguaMaxima!,
        version: this.version!,
      })
      .subscribe({
        next: (limites) => {
          this.aplicar(limites);
          this.saving.set(false);
          this.snackBar.open('Configuración guardada.', 'Cerrar', { duration: 4000 });
        },
        error: (err: HttpErrorResponse) => {
          this.saveError.set(getApiErrorMessage(err, 'No se pudo guardar la configuración.'));
          this.saving.set(false);
        },
      });
  }

  private aplicar(limites: LimitesCalidad): void {
    this.form.reset({
      acidezMaxima: limites.acidezMaxima,
      temperaturaMaxima: limites.temperaturaMaxima,
      aguaMaxima: limites.aguaMaxima,
    });
    this.actualizadoEn.set(limites.actualizadoEn);
    this.version = limites.version;
  }
}
