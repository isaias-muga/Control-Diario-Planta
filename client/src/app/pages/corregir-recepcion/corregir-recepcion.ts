import { Component, ElementRef, Injector, afterNextRender, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { forkJoin, map, startWith } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { NumeroInput } from '../../components/numero-input/numero-input';
import { crearFormularioRecepcion } from '../carga-rapida/formulario-recepcion';
import { RecepcionDetalle } from '../../models/correcciones';
import { MOTIVO_MAX_LENGTH, MOTIVO_MIN_LENGTH, OBSERVACIONES_MAX_LENGTH, FECHA_MIN, TICKET_MAX, TICKET_MIN } from '../../models/limits';
import { Proveedor } from '../../models/proveedor';
import { ConfiguracionService } from '../../services/configuracion';
import { ProveedoresService } from '../../services/proveedores';
import { RecepcionesService } from '../../services/recepciones';
import { getApiErrorField, getApiErrorMessage } from '../../utils/api-error';
import { CambioMostrado, cambiosDeRecepcion } from '../../utils/diff-recepcion';
import { etiquetaCampo } from '../../utils/historial';
import { motivoSuficiente } from '../../utils/motivo';
import { errorNumero } from '../../utils/numero-es';

type Paso = 'editar' | 'confirmar' | 'listo';

/** Why the last send did not end in a saved correction and the screen is still on the confirmation. */
interface Fallo {
  mensaje: string;
  /** The same data may be sent again. False when a retry answered 409: the first send was probably saved. */
  puedeReintentar: boolean;
}

/**
 * Corrects a recepción (Admin only), in two steps like the registration flow: edit with the same form definition and rules, then a
 * confirmation that lists old to new for every field that changed, with the mandatory reason. The old values stay in its history.
 */
@Component({
  selector: 'app-corregir-recepcion',
  imports: [ReactiveFormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, NumeroInput],
  templateUrl: './corregir-recepcion.html',
  styleUrl: './corregir-recepcion.css',
})
export class CorregirRecepcion {
  private recepcionesService = inject(RecepcionesService);
  private proveedoresService = inject(ProveedoresService);
  private configuracionService = inject(ConfiguracionService);
  private fb = inject(FormBuilder);
  private injector = inject(Injector);

  /** The recepción to correct (from the route). */
  id = input.required<string>();

  readonly observacionesMaxLength = OBSERVACIONES_MAX_LENGTH;
  readonly motivoMinLength = MOTIVO_MIN_LENGTH;
  readonly motivoMaxLength = MOTIVO_MAX_LENGTH;
  protected readonly fechaMin = FECHA_MIN;
  protected readonly errorNumero = errorNumero;
  protected readonly etiqueta = etiquetaCampo;

  form = crearFormularioRecepcion(this.fb);
  motivo = new FormControl('', { nonNullable: true, validators: [motivoSuficiente, Validators.maxLength(MOTIVO_MAX_LENGTH)] });

  paso = signal<Paso>('editar');
  detalle = signal<RecepcionDetalle | null>(null);
  proveedores = signal<Proveedor[]>([]);
  /** Detail, proveedores and the number of silos have arrived: until then the controls that need them are not offered. */
  cargado = signal(false);
  errorCarga = signal<string | null>(null);
  notFound = signal(false);
  /** A message shown on the first step: the API rejected the correction, or nothing was changed. */
  errorEditar = signal<string | null>(null);
  enviando = signal(false);
  fallo = signal<Fallo | null>(null);
  /** What was saved, as the confirmation listed it. */
  corregidos = signal<CambioMostrado[]>([]);
  private cantidadSilos = signal(0);
  /** True once a send ended without an answer (network, 5xx): the next send is a retry. */
  private reintento = false;

  private titulo = viewChild<ElementRef<HTMLElement>>('titulo');

  private valores = toSignal(
    this.form.valueChanges.pipe(
      startWith(null),
      map(() => this.form.getRawValue()),
    ),
    { requireSync: true },
  );

  /** Active proveedores, plus the recepción's own even if it was deactivated since (it keeps it unless changed). */
  protected proveedoresOfrecidos = computed(() => {
    const actual = this.detalle()?.proveedorId;
    return this.proveedores()
      .filter((p) => p.activo || p.id === actual)
      .sort((a, b) => a.nombre.localeCompare(b.nombre, 'es'));
  });

  /** 1..N from the configuration, and the recepción's own silo if the plant has fewer silos now (history is not re-checked). */
  protected silos = computed(() => {
    const maximo = Math.max(this.cantidadSilos(), this.detalle()?.silo ?? 0);
    return Array.from({ length: maximo }, (_, i) => i + 1);
  });

  /** Old to new for every field that changed (what the confirmation lists). */
  protected cambios = computed<CambioMostrado[]>(() => {
    const d = this.detalle();
    if (!d) {
      return [];
    }
    const v = this.valores();
    const nuevoProveedor = this.proveedores().find((p) => p.id === v.proveedorId)?.nombre ?? '';
    return cambiosDeRecepcion(
      d,
      {
        proveedorId: v.proveedorId ?? '',
        litros: v.litros,
        grasa: v.grasa,
        acidez: v.acidez,
        temperatura: v.temperatura,
        aguaPorcentaje: v.aguaPorcentaje,
        silo: v.silo,
        ticket: v.ticket,
        fechaHora: v.fechaHora ?? '',
        observaciones: v.observaciones,
      },
      nuevoProveedor,
    );
  });

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => this.cargar(id));
    });
  }

  protected cargar(id: string): void {
    this.cargado.set(false);
    this.errorCarga.set(null);
    this.notFound.set(false);
    forkJoin({
      detalle: this.recepcionesService.getDetalle(id),
      proveedores: this.proveedoresService.getAll(),
      configuracion: this.configuracionService.getCalidad(),
    }).subscribe({
      next: ({ detalle, proveedores, configuracion }) => {
        this.detalle.set(detalle);
        this.proveedores.set(proveedores);
        this.cantidadSilos.set(configuracion.cantidadSilos);
        this.llenar(detalle);
        this.cargado.set(true);
      },
      error: (err: HttpErrorResponse) => {
        if (err.status === 404 || err.status === 400) {
          this.notFound.set(true); // 400: the id in the URL is not a valid Guid
        } else {
          this.errorCarga.set(getApiErrorMessage(err, 'No se pudo cargar la recepción.'));
        }
      },
    });
  }

  private llenar(d: RecepcionDetalle): void {
    this.form.reset({
      proveedorId: d.proveedorId,
      fechaHora: d.fechaHora.slice(0, 16),
      litros: d.litros,
      grasa: d.grasa,
      acidez: d.acidez,
      temperatura: d.temperatura,
      silo: d.silo,
      aguaPorcentaje: d.aguaPorcentaje,
      ticket: d.ticket,
      observaciones: d.observaciones ?? '',
    });
    // An existing ticket stays: it cannot be removed (it would free a number that was used). Without one it stays optional.
    this.form.controls.ticket.setValidators([
      Validators.min(TICKET_MIN),
      Validators.max(TICKET_MAX),
      Validators.pattern(/^\d+$/),
      ...(d.ticket != null ? [Validators.required] : []),
    ]);
    this.form.controls.ticket.updateValueAndValidity({ emitEvent: false });
    this.motivo.reset('');
  }

  /** The silo buttons: choosing one sets the (required) silo of the form. */
  protected elegirSilo(silo: number): void {
    this.form.controls.silo.setValue(silo);
    this.form.controls.silo.markAsTouched();
  }

  invalid(control: keyof typeof this.form.controls): boolean {
    const c = this.form.controls[control];
    return c.touched && c.invalid;
  }

  protected continuar(): void {
    this.errorEditar.set(null);
    this.motivo.markAsTouched();
    if (this.form.invalid || this.motivo.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    if (this.cambios().length === 0) {
      this.errorEditar.set('No cambiaste ningún dato.');
      return;
    }
    this.irA('confirmar');
  }

  protected volverAEditar(): void {
    this.fallo.set(null);
    this.reintento = false;
    this.irA('editar');
  }

  /** Confirmar and Reintentar: the same data is sent with the version that was read. */
  protected enviar(): void {
    // A second tap can arrive before the disabled button is rendered
    if (this.enviando()) {
      return;
    }
    const d = this.detalle()!;
    const v = this.form.getRawValue();
    this.enviando.set(true);
    this.fallo.set(null);
    const lista = this.cambios();
    this.recepcionesService
      .corregir(d.id, {
        proveedorId: v.proveedorId!,
        litros: v.litros!,
        grasa: v.grasa,
        acidez: v.acidez!,
        temperatura: v.temperatura!,
        aguaPorcentaje: v.aguaPorcentaje,
        silo: v.silo!,
        ticket: v.ticket,
        fechaHora: v.fechaHora!,
        observaciones: v.observaciones?.trim() ? v.observaciones.trim() : null,
        motivo: this.motivo.value.trim(),
        version: d.version,
      })
      .subscribe({
        next: () => {
          this.enviando.set(false);
          this.corregidos.set(lista);
          this.irA('listo');
        },
        error: (err: HttpErrorResponse) => this.alFallar(err),
      });
  }

  private alFallar(err: HttpErrorResponse): void {
    this.enviando.set(false);
    const mensaje = getApiErrorMessage(err, 'No se pudo guardar la corrección.');
    if (err.status === 0 || err.status >= 500) {
      // No answer or a server error: the correction may or may not have been saved
      this.reintento = true;
      this.fallo.set({ mensaje, puedeReintentar: true });
      return;
    }
    if (err.status === 409 && this.reintento) {
      // A 409 on the retry means the first send was probably saved (the version it read is no longer the current one)
      this.fallo.set({
        mensaje: 'Probablemente la corrección ya se guardó. Revisá el historial de la recepción en Recepciones antes de volver a intentarlo.',
        puedeReintentar: false,
      });
      return;
    }
    // The server refused it (400, 409 duplicate ticket or a recepción changed by someone else): back to the form with the message
    if (getApiErrorField(err) === 'motivo') {
      this.motivo.markAsTouched();
    }
    this.errorEditar.set(
      err.status === 409 && !/ticket/i.test(mensaje) ? `${mensaje} Volvé a abrir la recepción para ver los datos actuales.` : mensaje,
    );
    this.irA('editar');
  }

  private irA(paso: Paso): void {
    this.paso.set(paso);
    // Move the focus to the title of the new step, so a screen reader announces it and the page starts at the top
    afterNextRender(
      () => {
        window.scrollTo({ top: 0 });
        this.titulo()?.nativeElement.focus();
      },
      { injector: this.injector },
    );
  }
}
