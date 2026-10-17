import {
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { DecimalPipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { map, startWith } from 'rxjs';
import { ProveedoresService } from '../../services/proveedores';
import { RecepcionesService } from '../../services/recepciones';
import { ConfiguracionService } from '../../services/configuracion';
import { ProveedorOpcion, RegistrarRecepcion } from '../../models/proveedor';
import { LimitesCalidad } from '../../models/configuracion';
import { OBSERVACIONES_MAX_LENGTH } from '../../models/limits';
import { crearFormularioRecepcion } from './recepcion-form-group';
import { getApiErrorField, getApiErrorMessage } from '../../utils/api-error';
import { litrosReales, motivosFueraDeRango } from '../../utils/calidad';
import { errorNumero } from '../../utils/numero-es';
import { NumeroInput } from '../numero-input/numero-input';
import { ahoraEnPlanta } from '../../utils/periodos';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';

@Component({
  selector: 'app-recepcion-form',
  imports: [
    ReactiveFormsModule,
    DecimalPipe,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    NumeroInput,
  ],
  templateUrl: './recepcion-form.html',
  styleUrl: './recepcion-form.css',
})
export class RecepcionForm {
  private proveedoresService = inject(ProveedoresService);
  private recepcionesService = inject(RecepcionesService);
  private configuracionService = inject(ConfiguracionService);
  private fb = inject(FormBuilder);
  private snackBar = inject(MatSnackBar);

  /** The proveedor of the recepción. Without it the form shows a selector to choose one. */
  proveedorId = input<string | null>(null);
  /** Emits after a successful save; `true` when the operator asked to keep loading ("Registrar y cargar otra"). */
  registrada = output<boolean>();

  readonly observacionesMaxLength = OBSERVACIONES_MAX_LENGTH;

  form = crearFormularioRecepcion(this.fb);

  protected readonly errorNumero = errorNumero;

  saving = signal(false);
  saveError = signal<string | null>(null);

  proveedores = signal<ProveedorOpcion[]>([]);
  proveedoresError = signal<string | null>(null);
  /** True while the ticket field still holds the number the API suggested (it can be edited). */
  ticketSugerido = signal(false);
  private limites = signal<LimitesCalidad | null>(null);

  private litrosInput = viewChild<ElementRef<HTMLInputElement>>('litrosInput');
  private proveedoresCargados = false;

  private valores = toSignal(
    this.form.valueChanges.pipe(
      startWith(null),
      map(() => this.form.getRawValue()),
    ),
    { requireSync: true },
  );

  /** Live preview: the liters minus the water. Null until the liters are a valid number. */
  litrosRealesPrevios = computed(() => {
    const { litros, aguaPorcentaje } = this.valores();
    if (litros == null || !Number.isFinite(litros) || litros <= 0) {
      return null;
    }
    return litrosReales(litros, aguaPorcentaje);
  });

  /** Live preview of the quality limits the reading exceeds; null while the limits are not loaded. */
  motivosPrevios = computed(() => {
    const limites = this.limites();
    if (!limites) {
      return null;
    }
    const { acidez, temperatura, aguaPorcentaje } = this.valores();
    return motivosFueraDeRango(limites, { acidez, temperatura, aguaPorcentaje });
  });

  constructor() {
    effect(() => {
      const id = this.proveedorId();
      untracked(() => {
        if (id) {
          this.form.controls.proveedorId.setValue(id);
        } else {
          this.cargarProveedores();
        }
      });
    });
    this.cargarSiguienteTicket();
    this.configuracionService.getCalidad().subscribe({
      next: (limites) => this.limites.set(limites),
      // The preview is a convenience: without the limits it just does not show the check.
      error: () => this.limites.set(null),
    });
  }

  /**
   * True when the operator has typed or changed something that would be lost by closing: any reading or
   * observation, or a proveedor, ticket or date they touched. The suggested ticket and the default date alone are not data.
   */
  tieneDatos(): boolean {
    const v = this.form.getRawValue();
    const lectura = [v.litros, v.grasa, v.acidez, v.temperatura, v.silo, v.aguaPorcentaje].some((x) => x != null);
    // Text the parser rejected leaves null in the control, but it is still something the operator typed
    const textoInvalido = Object.values(this.form.controls).some((c) => c.errors?.['numero']);
    const tocado = ['proveedorId', 'ticket', 'fechaHora'].some(
      (c) => this.form.controls[c as 'proveedorId' | 'ticket' | 'fechaHora'].dirty,
    );
    return lectura || textoInvalido || (v.observaciones ?? '').trim() !== '' || tocado;
  }

  invalid(control: keyof typeof this.form.controls): boolean {
    const c = this.form.controls[control];
    return c.touched && c.invalid;
  }

  submit(cargarOtra = false): void {
    // A second click can arrive before the disabled button is rendered
    if (this.saving()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const v = this.form.getRawValue();
    const recepcion: RegistrarRecepcion = {
      fechaHora: v.fechaHora!,
      litros: v.litros!,
      grasa: v.grasa,
      acidez: v.acidez!,
      temperatura: v.temperatura!,
      silo: v.silo,
      aguaPorcentaje: v.aguaPorcentaje,
      ticket: v.ticket,
      observaciones: v.observaciones?.trim() || null,
    };

    this.saving.set(true);
    this.saveError.set(null);

    this.proveedoresService.registrarRecepcion(v.proveedorId!, recepcion).subscribe({
      next: () => {
        this.form.reset({
          proveedorId: v.proveedorId,
          fechaHora: ahoraEnPlanta(),
          observaciones: '',
        });
        this.ticketSugerido.set(false);
        this.saving.set(false);
        this.snackBar.open('Recepción registrada.', 'Cerrar', { duration: 4000 });
        if (cargarOtra) {
          this.cargarSiguienteTicket();
          this.litrosInput()?.nativeElement.focus();
        }
        this.registrada.emit(cargarOtra);
      },
      error: (err: HttpErrorResponse) => {
        const mensaje = getApiErrorMessage(err, 'No se pudo registrar la recepción.');
        if (err.status === 409) {
          // Duplicate ticket: show it next to the ticket field (cleared when the user edits it)
          this.form.controls.ticket.setErrors({ duplicado: mensaje });
          this.form.controls.ticket.markAsTouched();
        } else if (this.mostrarEnCampo(getApiErrorField(err), mensaje)) {
          // Field-level 400 (e.g. fechaHora): shown next to the field
        } else {
          this.saveError.set(mensaje);
        }
        this.saving.set(false);
      },
    });
  }

  protected mensajeFechaHora(): string {
    const errores = this.form.controls.fechaHora.errors;
    if (errores?.['servidor']) {
      return errores['servidor'];
    }
    if (errores?.['fechaMin']) {
      return 'No puede ser anterior al 01/01/2000.';
    }
    if (errores?.['fechaMax']) {
      return 'No puede superar la hora actual en más de 1 hora.';
    }
    return 'Requerido.';
  }

  protected etiquetaMotivos(motivos: string[]): string {
    return motivos.map((m) => m.toLowerCase()).join(', ');
  }

  private cargarSiguienteTicket(): void {
    this.recepcionesService.getSiguienteTicket().subscribe({
      next: ({ ticket }) => {
        // Never overwrite what the operator already typed while the suggestion was loading
        if (ticket != null && this.form.controls.ticket.value == null && !this.form.controls.ticket.errors?.['numero']) {
          this.form.controls.ticket.setValue(ticket);
          this.ticketSugerido.set(true);
        }
      },
      // The suggestion is optional: without it the operator types the ticket.
      error: () => this.ticketSugerido.set(false),
    });
  }

  private cargarProveedores(): void {
    if (this.proveedoresCargados) {
      return;
    }
    this.proveedoresCargados = true;
    this.proveedoresService.getOpciones().subscribe({
      next: (proveedores) =>
        this.proveedores.set([...proveedores].sort((a, b) => a.nombre.localeCompare(b.nombre, 'es'))),
      error: (err: HttpErrorResponse) =>
        this.proveedoresError.set(getApiErrorMessage(err, 'No se pudieron cargar los proveedores.')),
    });
  }

  /** Puts an API message on the control it names (cleared when the user edits it); false if there is none. */
  private mostrarEnCampo(campo: string | null, mensaje: string): boolean {
    // Only fechaHora renders the server's text next to the field; any other field goes to the generic error.
    const control = campo === 'fechaHora' ? this.form.controls.fechaHora : null;
    if (!control) {
      return false;
    }
    control.setErrors({ servidor: mensaje });
    control.markAsTouched();
    return true;
  }
}
