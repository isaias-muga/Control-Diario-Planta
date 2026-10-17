import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { DecimalPipe } from '@angular/common';
import { FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { map, startWith } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { crearFormularioRecepcion } from '../../components/recepcion-form/recepcion-form-group';
import { NumeroInput } from '../../components/numero-input/numero-input';
import { LimitesCalidad } from '../../models/configuracion';
import { OBSERVACIONES_MAX_LENGTH, PIN_LENGTH } from '../../models/limits';
import { ProveedorOpcion, RegistrarRecepcion } from '../../models/proveedor';
import { AuthService } from '../../services/auth';
import { ConfiguracionService } from '../../services/configuracion';
import { ProveedoresService } from '../../services/proveedores';
import { RecepcionesService } from '../../services/recepciones';
import { getApiErrorField, getApiErrorMessage } from '../../utils/api-error';
import { litrosReales, motivosFueraDeRango } from '../../utils/calidad';
import { errorNumero } from '../../utils/numero-es';
import { ahoraEnPlanta, formatearFecha } from '../../utils/periodos';

function textoFechaHora(fechaHora: string): string {
  return `${formatearFecha(fechaHora.slice(0, 10))} ${fechaHora.slice(11, 16)}`;
}

type Paso = 'carga' | 'confirmar' | 'listo';
type Medicion = 'acidez' | 'temperatura' | 'agua';

/** What the server saved, as it answered: the ticket and liters as entered, the proveedor and who identified as the operario. */
interface Cargada {
  ticket: number | null;
  litros: number;
  proveedor: string;
  operario: string | null;
}

/** Why the last send did not end in a saved recepción and the screen is still on the confirmation. */
interface Fallo {
  mensaje: string;
  /** The same data may be sent again. False when a retry answered 409: the first send was probably saved. */
  puedeReintentar: boolean;
}

@Component({
  selector: 'app-carga-rapida',
  imports: [
    ReactiveFormsModule,
    DecimalPipe,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    NumeroInput,
  ],
  templateUrl: './carga-rapida.html',
  styleUrl: './carga-rapida.css',
})
export class CargaRapida {
  protected auth = inject(AuthService);
  private proveedoresService = inject(ProveedoresService);
  private recepcionesService = inject(RecepcionesService);
  private configuracionService = inject(ConfiguracionService);
  private fb = inject(FormBuilder);
  private injector = inject(Injector);
  private destroyRef = inject(DestroyRef);

  readonly observacionesMaxLength = OBSERVACIONES_MAX_LENGTH;
  protected readonly errorNumero = errorNumero;

  form = crearFormularioRecepcion(this.fb);

  paso = signal<Paso>('carga');
  enviando = signal(false);
  /** Message shown on step 1 (duplicate ticket, or something the API rejected). */
  errorCarga = signal<string | null>(null);
  fallo = signal<Fallo | null>(null);
  cargada = signal<Cargada | null>(null);
  proveedores = signal<ProveedorOpcion[]>([]);

  /** The operario PIN, asked on the confirmation of the reception phone (masked, numeric keyboard). Never kept after a send. */
  pin = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(new RegExp(`^\\d{${PIN_LENGTH}}$`))] });
  protected readonly pinLength = PIN_LENGTH;
  proveedoresError = signal<string | null>(null);
  /** The options have arrived: an empty select ignores the tap in silence, so it is not shown before this. */
  proveedoresCargados = signal(false);
  private limites = signal<LimitesCalidad | null>(null);
  /** True once a send ended without an answer (network, 5xx): the next send is a retry that may duplicate it. */
  private reintento = false;

  private titulo = viewChild<ElementRef<HTMLElement>>('titulo');

  private valores = toSignal(
    this.form.valueChanges.pipe(
      startWith(null),
      map(() => this.form.getRawValue()),
    ),
    { requireSync: true },
  );

  protected proveedorNombre = computed(
    () => this.proveedores().find((p) => p.id === this.valores().proveedorId)?.nombre ?? '',
  );

  /** The liters minus the water: a preview for the confirmation, the API stores the real figure. */
  protected litrosRealesPrevios = computed(() => {
    const { litros, aguaPorcentaje } = this.valores();
    return litros == null ? null : litrosReales(litros, aguaPorcentaje);
  });

  /** Which readings exceed the quality limits; empty while the limits are not loaded. */
  private motivos = computed(() => {
    const limites = this.limites();
    if (!limites) {
      return [];
    }
    const { acidez, temperatura, aguaPorcentaje } = this.valores();
    return motivosFueraDeRango(limites, { acidez, temperatura, aguaPorcentaje });
  });

  protected fechaHoraTexto = computed(() => textoFechaHora(this.valores().fechaHora ?? ''));

  /** Plant time shown in the help line of step 1; the moment actually saved is fixed on "Revisar datos". */
  private ahora = signal(ahoraEnPlanta());
  protected ahoraTexto = computed(() => textoFechaHora(this.ahora()));

  constructor() {
    // The ticket is required here: the operator copies it from the plant's paper ticket
    this.form.controls.ticket.addValidators(Validators.required);
    // Adding a validator does not run it: without this the empty ticket would be valid until touched
    this.form.controls.ticket.updateValueAndValidity({ emitEvent: false });
    this.proveedoresService.getOpciones().subscribe({
      next: (lista) => {
        this.proveedores.set([...lista].sort((a, b) => a.nombre.localeCompare(b.nombre, 'es')));
        this.proveedoresCargados.set(true);
      },
      error: (err: HttpErrorResponse) =>
        this.proveedoresError.set(getApiErrorMessage(err, 'No se pudieron cargar los proveedores.')),
    });
    this.configuracionService.getCalidad().subscribe({
      next: (limites) => this.limites.set(limites),
      // Without the limits the confirmation just does not highlight anything.
      error: () => this.limites.set(null),
    });
    this.cargarSiguienteTicket();
    // The help line says "ahora": keep it from going stale while the operator types
    const reloj = setInterval(() => this.ahora.set(ahoraEnPlanta()), 30_000);
    this.destroyRef.onDestroy(() => clearInterval(reloj));
  }

  protected fueraDeRango(medicion: Medicion): boolean {
    const motivo = { acidez: 'Acidez', temperatura: 'Temperatura', agua: 'Agua' }[medicion];
    return this.motivos().some((m) => m === motivo);
  }

  /** The limit a reading is compared with, for the red hint on the confirmation. */
  protected limite(medicion: Medicion): number | null {
    const limites = this.limites();
    if (!limites) {
      return null;
    }
    return { acidez: limites.acidezMaxima, temperatura: limites.temperaturaMaxima, agua: limites.aguaMaxima }[medicion];
  }

  invalid(control: keyof typeof this.form.controls): boolean {
    const c = this.form.controls[control];
    return c.touched && c.invalid;
  }

  continuar(): void {
    this.errorCarga.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    // The fecha/hora shown (and later sent) is fixed here, so a retry sends the same moment
    this.form.controls.fechaHora.setValue(ahoraEnPlanta());
    this.irA('confirmar');
  }

  corregir(): void {
    this.fallo.set(null);
    this.reintento = false;
    this.pin.reset();
    this.ahora.set(ahoraEnPlanta());
    this.irA('carga');
  }

  nuevaRecepcion(): void {
    this.form.reset({ proveedorId: '', fechaHora: ahoraEnPlanta(), observaciones: '' });
    this.cargada.set(null);
    this.fallo.set(null);
    this.errorCarga.set(null);
    this.reintento = false;
    this.pin.reset();
    this.cargarSiguienteTicket();
    this.ahora.set(ahoraEnPlanta());
    this.irA('carga');
  }

  /** Confirmar and Reintentar: the same data (and fecha/hora) is sent. */
  enviar(): void {
    // A second tap can arrive before the disabled button is rendered
    if (this.enviando()) {
      return;
    }
    const pideLaClave = this.auth.esRecepcion();
    if (pideLaClave && this.pin.invalid) {
      this.pin.markAsTouched();
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
      pin: pideLaClave ? this.pin.value : null,
    };
    const eraReintento = this.reintento;

    this.enviando.set(true);
    this.fallo.set(null);
    this.proveedoresService.registrarRecepcion(v.proveedorId!, recepcion).subscribe({
      next: (guardada) => {
        this.pin.reset();
        this.cargada.set({ ticket: guardada.ticket, litros: guardada.litros, proveedor: guardada.proveedorNombre, operario: guardada.operarioNombre });
        this.reintento = false;
        this.enviando.set(false);
        this.irA('listo');
      },
      error: (err: HttpErrorResponse) => {
        this.enviando.set(false);
        const mensaje = getApiErrorMessage(err, 'No se pudo registrar la recepción.');
        if (err.status === 429 || (err.status === 400 && getApiErrorField(err) === 'pin')) {
          // Nothing was saved: a wrong PIN, or too many wrong ones. The operario types it again on this same screen.
          this.pin.reset();
          this.pin.setErrors({ servidor: mensaje });
          this.pin.markAsTouched();
        } else if (err.status === 409 && eraReintento) {
          // The first send may have reached the server before the connection failed
          this.fallo.set({
            mensaje: 'Es probable que ya se haya guardado. Consultá en la oficina antes de cargarla de nuevo.',
            puedeReintentar: false,
          });
        } else if (err.status === 409) {
          this.pin.reset();
          this.errorCarga.set('Ese ticket ya fue cargado. Consultá en la oficina.');
          this.form.controls.ticket.markAsTouched();
          this.irA('carga');
        } else if (err.status === 400) {
          // Nothing was saved and the data has to change; the next send is a first send again
          this.reintento = false;
          this.pin.reset();
          this.errorCarga.set(mensaje);
          this.irA('carga');
        } else {
          this.reintento = true;
          this.fallo.set({ mensaje, puedeReintentar: true });
        }
      },
    });
  }

  private cargarSiguienteTicket(): void {
    this.recepcionesService.getSiguienteTicket().subscribe({
      next: ({ ticket }) => {
        // Never overwrite what the operator already typed while the suggestion was loading
        if (ticket != null && this.form.controls.ticket.value == null && !this.form.controls.ticket.errors?.['numero']) {
          this.form.controls.ticket.setValue(ticket);
        }
      },
      // The suggestion is optional: without it the operator types the ticket.
      error: () => undefined,
    });
  }

  /** Changes the step and moves the focus to its heading, so a screen reader announces the new screen. */
  private irA(paso: Paso): void {
    this.paso.set(paso);
    window.scrollTo(0, 0);
    afterNextRender(() => this.titulo()?.nativeElement.focus(), { injector: this.injector });
  }
}
