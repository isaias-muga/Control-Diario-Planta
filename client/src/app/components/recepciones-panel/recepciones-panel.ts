import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { LimitesCalidad } from '../../models/configuracion';
import { Proveedor } from '../../models/proveedor';
import {
  FiltroRecepciones,
  ListadoRecepciones,
  RecepcionListado,
  SubtotalDia,
  TotalesListado,
} from '../../models/recepcion-listado';
import { ConfiguracionService } from '../../services/configuracion';
import { ProveedoresService } from '../../services/proveedores';
import { RecepcionesService } from '../../services/recepciones';
import { getApiErrorMessage } from '../../utils/api-error';
import { AtajoPeriodo, hoyEnPlanta, rangoDeAtajo } from '../../utils/periodos';
import { RecepcionesFiltros } from '../recepciones-filtros/recepciones-filtros';
import { RecepcionesList } from '../recepciones-list/recepciones-list';

const FECHA_ISO = /^\d{4}-\d{2}-\d{2}$/;

function esFechaValida(valor: string | null): valor is string {
  if (!valor || !FECHA_ISO.test(valor)) {
    return false;
  }
  const [anio, mes, dia] = valor.split('-').map(Number);
  const fecha = new Date(Date.UTC(anio, mes - 1, dia));
  return fecha.getUTCFullYear() === anio && fecha.getUTCMonth() === mes - 1 && fecha.getUTCDate() === dia;
}

/**
 * Filters (synced with the URL query params), totals and the paged list of recepciones. It is shared by the
 * Recepciones page and the proveedor page (which fixes the proveedor and hides its selector).
 */
@Component({
  selector: 'app-recepciones-panel',
  imports: [
    DecimalPipe,
    RouterLink,
    MatCardModule,
    MatProgressSpinnerModule,
    RecepcionesFiltros,
    RecepcionesList,
  ],
  templateUrl: './recepciones-panel.html',
  styleUrl: './recepciones-panel.css',
})
export class RecepcionesPanel {
  private recepcionesService = inject(RecepcionesService);
  private proveedoresService = inject(ProveedoresService);
  private configuracionService = inject(ConfiguracionService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private request?: Subscription;
  private requestMas?: Subscription;

  /** When set, the list is fixed to this proveedor (and the proveedor selector and column are hidden). */
  proveedorFijo = input<string | null>(null);
  /** The period used when the URL does not say one. */
  atajoInicial = input<AtajoPeriodo>('hoy');
  /** Emits the filters in effect (the page uses them for its subtitle). */
  filtrosChange = output<FiltroRecepciones>();

  private params = toSignal(this.route.queryParamMap, { requireSync: true });

  /** The filters in effect, read from the URL (the source of truth) with the defaults for what it does not say. */
  protected filtros = computed<FiltroRecepciones>(() => {
    const p = this.params();
    const porDefecto = rangoDeAtajo(this.atajoInicial(), hoyEnPlanta());
    const desde = p.get('desde');
    const hasta = p.get('hasta');
    const rango = esFechaValida(desde) && esFechaValida(hasta) ? { desde, hasta } : porDefecto;
    const ticket = p.get('ticket');
    return {
      ...rango,
      proveedorId: this.proveedorFijo() ?? (p.get('proveedor') || null),
      ticket: ticket && /^\d+$/.test(ticket) ? Number(ticket) : null,
      soloFueraDeRango: p.get('fuera') === '1',
      incluirAnuladas: p.get('anuladas') === '1',
    };
  });

  proveedores = signal<Proveedor[]>([]);
  protected limites = signal<LimitesCalidad | null>(null);
  protected items = signal<RecepcionListado[]>([]);
  protected dias = signal<SubtotalDia[]>([]);
  protected totales = signal<TotalesListado | null>(null);
  protected hayMas = signal(false);
  protected pagina = signal(1);
  protected loading = signal(true);
  protected cargandoMas = signal(false);
  protected error = signal<string | null>(null);
  protected errorMas = signal<string | null>(null);

  /** Proveedores offered by the selector; null when the proveedor is fixed. */
  protected proveedoresParaSelector = computed(() => (this.proveedorFijo() ? null : this.proveedores()));

  protected litrosPorAgua = computed(() => {
    const t = this.totales();
    return t ? t.litros - t.litrosReales : 0;
  });

  protected motivosTotales = computed(() => {
    const t = this.totales();
    if (!t) {
      return [];
    }
    return [
      { cantidad: t.fueraPorTemperatura, etiqueta: 'temperatura' },
      { cantidad: t.fueraPorAcidez, etiqueta: 'acidez' },
      { cantidad: t.fueraPorAgua, etiqueta: 'agua' },
    ].filter((m) => m.cantidad > 0);
  });

  constructor() {
    this.configuracionService.getCalidad().subscribe({
      next: (limites) => this.limites.set(limites),
      error: () => this.limites.set(null), // the footer just omits the limits
    });

    effect(() => {
      if (this.proveedorFijo()) {
        return;
      }
      untracked(() =>
        this.proveedoresService.getAll().subscribe({
          next: (lista) => this.proveedores.set(lista),
          error: () => this.proveedores.set([]), // the filter simply offers "Todos"
        }),
      );
    });

    effect(() => {
      const filtros = this.filtros();
      this.filtrosChange.emit(filtros);
      untracked(() => this.cargar(filtros));
    });
  }

  /** Reloads the first page with the current filters (after a recepción was registered). */
  recargar(): void {
    this.cargar(this.filtros(), false);
  }

  protected cambiarFiltros(filtro: FiltroRecepciones): void {
    // Each filter is a query param; defaults and "off" values stay out of the URL.
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        desde: filtro.desde,
        hasta: filtro.hasta,
        proveedor: this.proveedorFijo() ? null : filtro.proveedorId || null,
        ticket: filtro.ticket ?? null,
        fuera: filtro.soloFueraDeRango ? '1' : null,
        anuladas: filtro.incluirAnuladas ? '1' : null,
      },
      replaceUrl: true,
    });
  }

  protected cargarMas(): void {
    if (this.cargandoMas()) {
      return;
    }
    this.cargandoMas.set(true);
    this.errorMas.set(null);
    const siguiente = this.pagina() + 1;
    this.requestMas?.unsubscribe();
    this.requestMas = this.recepcionesService.getListado({ ...this.filtros(), pagina: siguiente }).subscribe({
      next: (data) => {
        // A recepción registered meanwhile shifts the pages by one: never show a row twice
        this.items.update((actuales) => {
          const ids = new Set(actuales.map((r) => r.id));
          return [...actuales, ...data.items.filter((r) => !ids.has(r.id))];
        });
        this.dias.update((actuales) => unirDias(actuales, data.dias));
        this.hayMas.set(data.hayMas);
        this.pagina.set(data.pagina);
        this.cargandoMas.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.errorMas.set(getApiErrorMessage(err, 'No se pudieron cargar más recepciones.'));
        this.cargandoMas.set(false);
      },
    });
  }

  private cargar(filtros: FiltroRecepciones, conSpinner = true): void {
    this.request?.unsubscribe();
    this.requestMas?.unsubscribe();
    this.cargandoMas.set(false);
    this.errorMas.set(null);
    if (conSpinner) {
      this.loading.set(true);
    }
    this.error.set(null);

    this.request = this.recepcionesService.getListado({ ...filtros, pagina: 1 }).subscribe({
      next: (data: ListadoRecepciones) => {
        this.items.set(data.items);
        this.dias.set(data.dias);
        this.totales.set(data.totales);
        this.hayMas.set(data.hayMas);
        this.pagina.set(data.pagina);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.items.set([]);
        this.dias.set([]);
        this.totales.set(null);
        this.hayMas.set(false);
        this.error.set(getApiErrorMessage(err, 'No se pudieron cargar las recepciones.'));
        this.loading.set(false);
      },
    });
  }
}

/** Day subtotals of a following page replace the ones already known for the same day (they cover the whole day). */
function unirDias(actuales: SubtotalDia[], nuevos: SubtotalDia[]): SubtotalDia[] {
  const fechas = new Set(nuevos.map((d) => d.fecha));
  return [...actuales.filter((d) => !fechas.has(d.fecha)), ...nuevos];
}
