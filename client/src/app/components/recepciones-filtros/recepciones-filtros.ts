import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ErrorStateMatcher, provideNativeDateAdapter } from '@angular/material/core';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { FECHA_MIN, RESUMEN_MAX_DIAS, TICKET_MAX, TICKET_MIN } from '../../models/limits';
import { Proveedor } from '../../models/proveedor';
import { errorNumero, parsearNumeroEs } from '../../utils/numero-es';
import { FiltroRecepciones } from '../../models/recepcion-listado';
import {
  AtajoPeriodo,
  atajoDeRango,
  dateAIso,
  diasEnRango,
  hoyEnPlanta,
  isoADate,
  rangoDeAtajo,
} from '../../utils/periodos';

/** The ticket search applies by itself while typing, so its error shows as soon as the value is invalid (not on blur). */
export const errorAlEscribir: ErrorStateMatcher = {
  isErrorState: (control) => !!control && control.invalid && control.dirty,
};

/** Filters of the recepciones list: quick period, custom range, proveedor, ticket and "solo fuera de rango". */
@Component({
  selector: 'app-recepciones-filtros',
  imports: [
    ReactiveFormsModule,
    MatButtonToggleModule,
    MatCheckboxModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
  ],
  providers: [provideNativeDateAdapter()],
  templateUrl: './recepciones-filtros.html',
  styleUrl: './recepciones-filtros.css',
})
export class RecepcionesFiltros {
  private fb = inject(FormBuilder).nonNullable;

  filtros = input.required<FiltroRecepciones>();
  /** The proveedores to choose from; null hides the selector (the proveedor is fixed by the page). */
  proveedores = input<Proveedor[] | null>(null);
  cambio = output<FiltroRecepciones>();

  protected readonly TODOS = 'todos'; // value of the "Todos" option (mat-select ignores null as a selection)
  protected readonly ticketMax = TICKET_MAX;
  protected readonly errorAlEscribir = errorAlEscribir;
  protected readonly errorNumero = errorNumero;

  protected atajo = computed<AtajoPeriodo | null>(() =>
    atajoDeRango({ desde: this.filtros().desde, hasta: this.filtros().hasta }, hoyEnPlanta()),
  );
  protected errorRango = signal<string | null>(null);

  protected rangoForm = this.fb.group({
    inicio: this.fb.control<Date | null>(null),
    fin: this.fb.control<Date | null>(null),
  });
  protected proveedorControl = this.fb.control<string>(this.TODOS);
  // A string control (debounce, URL echo), not appNumero: it shares the parser, so "1.234" is ticket 1234.
  // Typed the es-AR way ("1.234" is ticket 1234; decimals are rejected). Whitespace around (or only whitespace)
  // counts as an empty search, not as an invalid ticket.
  protected ticketControl = this.fb.control<string>('', [
    (c) => {
      const resultado = parsearNumeroEs(String(c.value ?? ''), { entero: true });
      if (!resultado.ok) {
        return { numero: resultado.mensaje };
      }
      const ticket = resultado.valor;
      return ticket === null || (ticket >= TICKET_MIN && ticket <= TICKET_MAX) ? null : { ticket: true };
    },
  ]);
  protected soloControl = this.fb.control(false);
  protected anuladasControl = this.fb.control(false);
  /** The ticket this component last asked for: the URL echoing it back must not overwrite what is typed since. */
  private ticketEmitido: number | null | undefined;

  constructor() {
    // The URL is the source of truth: whenever the filters change from outside, the controls follow.
    effect(() => {
      const f = this.filtros();
      this.rangoForm.setValue(
        { inicio: isoADate(f.desde), fin: isoADate(f.hasta) },
        { emitEvent: false },
      );
      this.proveedorControl.setValue(f.proveedorId ?? this.TODOS, { emitEvent: false });
      this.soloControl.setValue(!!f.soloFueraDeRango, { emitEvent: false });
      this.anuladasControl.setValue(!!f.incluirAnuladas, { emitEvent: false });
      const ticket = f.ticket != null ? String(f.ticket) : '';
      if (f.ticket !== this.ticketEmitido && this.ticketControl.value.trim() !== ticket) {
        this.ticketControl.setValue(ticket, { emitEvent: false });
      }
      this.ticketEmitido = f.ticket ?? null;
    });

    this.ticketControl.valueChanges
      .pipe(debounceTime(400), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe(() => {
        if (this.ticketControl.valid) {
          this.emitir({});
        }
      });
  }

  protected seleccionarAtajo(atajo: AtajoPeriodo | undefined): void {
    if (!atajo) {
      return;
    }
    this.errorRango.set(null);
    this.emitir(rangoDeAtajo(atajo, hoyEnPlanta()));
  }

  /** Called when the end date of the range field changes (both ends are then set). */
  protected rangoPersonalizadoCambiado(): void {
    const { inicio, fin } = this.rangoForm.getRawValue();
    if (!inicio || !fin) {
      return;
    }
    const nuevo = { desde: dateAIso(inicio), hasta: dateAIso(fin) };
    if (nuevo.desde < FECHA_MIN.slice(0, 10)) {
      this.errorRango.set('La fecha desde no puede ser anterior al 01/01/2000.');
      return;
    }
    if (diasEnRango(nuevo.desde, nuevo.hasta) > RESUMEN_MAX_DIAS) {
      this.errorRango.set(`El período no puede superar los ${RESUMEN_MAX_DIAS} días.`);
      return;
    }
    this.errorRango.set(null);
    this.emitir(nuevo);
  }

  protected proveedorCambiado(): void {
    const id = this.proveedorControl.value;
    this.emitir({ proveedorId: id === this.TODOS ? null : id });
  }

  protected soloCambiado(): void {
    this.emitir({ soloFueraDeRango: this.soloControl.value });
  }

  protected anuladasCambiado(): void {
    this.emitir({ incluirAnuladas: this.anuladasControl.value });
  }

  private emitir(cambios: Partial<FiltroRecepciones>): void {
    // The ticket may still be waiting for its debounce: every change carries what is typed, so a change of
    // another filter right after typing the ticket does not lose it (the URL would not have it yet).
    const escrito = parsearNumeroEs(this.ticketControl.value, { entero: true });
    const ticket = this.ticketControl.valid && escrito.ok ? escrito.valor : this.filtros().ticket;
    this.ticketEmitido = cambios.ticket !== undefined ? cambios.ticket : ticket;
    this.cambio.emit({ ...this.filtros(), ticket, ...cambios, pagina: undefined });
  }
}
