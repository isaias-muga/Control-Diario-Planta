import {
  Component,
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
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
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
import { OBSERVACIONES_MAX_LENGTH } from '../../models/limits';
import { Proveedor, RegistrarRecepcion } from '../../models/proveedor';
import { ConfiguracionService } from '../../services/configuracion';
import { ProveedoresService } from '../../services/proveedores';
import { RecepcionesService } from '../../services/recepciones';
import { getApiErrorMessage } from '../../utils/api-error';
import { litrosReales, motivosFueraDeRango } from '../../utils/calidad';
import { errorNumero } from '../../utils/numero-es';
import { ahoraEnPlanta, formatearFecha } from '../../utils/periodos';

type Paso = 'carga' | 'confirmar' | 'listo';
type Medicion = 'acidez' | 'temperatura' | 'agua';

/** What the operator entered and the server accepted; the POST answers with the proveedor only. */
interface Cargada {
  ticket: number;
  litros: number;
  proveedor: string;
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
  private proveedoresService = inject(ProveedoresService);
  private recepcionesService = inject(RecepcionesService);
  private configuracionService = inject(ConfiguracionService);
  private fb = inject(FormBuilder);
  private injector = inject(Injector);

  readonly observacionesMaxLength = OBSERVACIONES_MAX_LENGTH;
  protected readonly errorNumero = errorNumero;

  form = crearFormularioRecepcion(this.fb);

  paso = signal<Paso>('carga');
  enviando = signal(false);
  /** Message shown on step 1 (duplicate ticket, or something the API rejected). */
  errorCarga = signal<string | null>(null);
  fallo = signal<Fallo | null>(null);
  cargada = signal<Cargada | null>(null);
  proveedores = signal<Proveedor[]>([]);
  proveedoresError = signal<string | null>(null);
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

  protected fechaHoraTexto = computed(() => {
    const fechaHora = this.valores().fechaHora ?? '';
    return `${formatearFecha(fechaHora.slice(0, 10))} ${fechaHora.slice(11, 16)}`;
  });

  constructor() {
    // The ticket is required here: the operator copies it from the plant's paper ticket
    this.form.controls.ticket.addValidators(Validators.required);
    // Adding a validator does not run it: without this the empty ticket would be valid until touched
    this.form.controls.ticket.updateValueAndValidity({ emitEvent: false });
    this.proveedoresService.getAll().subscribe({
      next: (lista) =>
        this.proveedores.set([...lista].sort((a, b) => a.nombre.localeCompare(b.nombre, 'es'))),
      error: (err: HttpErrorResponse) =>
        this.proveedoresError.set(getApiErrorMessage(err, 'No se pudieron cargar los proveedores.')),
    });
    this.configuracionService.getCalidad().subscribe({
      next: (limites) => this.limites.set(limites),
      // Without the limits the confirmation just does not highlight anything.
      error: () => this.limites.set(null),
    });
    this.cargarSiguienteTicket();
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
    this.irA('carga');
  }

  nuevaRecepcion(): void {
    this.form.reset({ proveedorId: '', fechaHora: ahoraEnPlanta(), observaciones: '' });
    this.cargada.set(null);
    this.fallo.set(null);
    this.errorCarga.set(null);
    this.reintento = false;
    this.cargarSiguienteTicket();
    this.irA('carga');
  }

  /** Confirmar and Reintentar: the same data (and fecha/hora) is sent. */
  enviar(): void {
    // A second tap can arrive before the disabled button is rendered
    if (this.enviando()) {
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
    const eraReintento = this.reintento;

    this.enviando.set(true);
    this.fallo.set(null);
    this.proveedoresService.registrarRecepcion(v.proveedorId!, recepcion).subscribe({
      next: () => {
        this.cargada.set({ ticket: v.ticket!, litros: v.litros!, proveedor: this.proveedorNombre() });
        this.reintento = false;
        this.enviando.set(false);
        this.irA('listo');
      },
      error: (err: HttpErrorResponse) => {
        this.enviando.set(false);
        const mensaje = getApiErrorMessage(err, 'No se pudo registrar la recepción.');
        if (err.status === 409 && eraReintento) {
          // The first send may have reached the server before the connection failed
          this.fallo.set({
            mensaje: 'Es probable que ya se haya guardado. Consultá en la oficina antes de cargarla de nuevo.',
            puedeReintentar: false,
          });
        } else if (err.status === 409) {
          this.errorCarga.set('Ese ticket ya fue cargado. Consultá en la oficina.');
          this.form.controls.ticket.markAsTouched();
          this.irA('carga');
        } else if (err.status === 400) {
          // Nothing was saved and the data has to change; the next send is a first send again
          this.reintento = false;
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
