import { Component, computed, inject, input, output } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuModule } from '@angular/material/menu';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import {
  MotivoFueraDeRango,
  RecepcionListado,
  SubtotalDia,
} from '../../models/recepcion-listado';
import { AuthService } from '../../services/auth';
import { RecepcionesService } from '../../services/recepciones';
import { textoFechaHora, textoInstante } from '../../utils/historial';
import { formatearFecha } from '../../utils/periodos';
import { HistorialDialog, HistorialDialogData } from '../historial-dialog/historial-dialog';
import { MotivoDialog, MotivoDialogData } from '../motivo-dialog/motivo-dialog';
import { ObservacionCelda } from '../observacion-celda/observacion-celda';
import {
  ObservacionesDialog,
  ObservacionesDialogData,
} from '../observaciones-dialog/observaciones-dialog';

interface FilaDia {
  tipo: 'dia';
  etiqueta: string;
  subtotal: SubtotalDia | null;
}

interface FilaRecepcion {
  tipo: 'recepcion';
  recepcion: RecepcionListado;
}

type Fila = FilaDia | FilaRecepcion;

const ETIQUETA_MOTIVO: Record<MotivoFueraDeRango, string> = {
  Acidez: 'Acidez alta',
  Temperatura: 'Temp. alta',
  Agua: 'Agua',
};

/** Recepciones grouped by day (newest first) with the day subtotals, in a table that follows the Tables convention. */
@Component({
  selector: 'app-recepciones-list',
  imports: [
    DecimalPipe,
    RouterLink,
    MatButtonModule,
    MatMenuModule,
    MatTableModule,
    MatTooltipModule,
    ObservacionCelda,
  ],
  templateUrl: './recepciones-list.html',
  styleUrl: './recepciones-list.css',
})
export class RecepcionesList {
  private dialog = inject(MatDialog);
  private snackBar = inject(MatSnackBar);
  private auth = inject(AuthService);
  private recepcionesService = inject(RecepcionesService);

  items = input.required<RecepcionListado[]>();
  /** Subtotals of the whole day, for the days that appear in `items`. */
  dias = input<SubtotalDia[]>([]);
  /** Hide the proveedor column where it is redundant (the proveedor's own page). */
  mostrarProveedor = input(true);
  hayMas = input(false);
  cargandoMas = input(false);
  cargarMas = output<void>();
  /** A recepción was voided from the row menu: the page reloads the list and its totals. */
  cambio = output<void>();

  protected columnas = computed(() => {
    const base = [
      'hora', 'ticket', 'proveedor', 'litros', 'agua', 'litrosReales',
      'grasa', 'acidez', 'temperatura', 'silo', 'estado', 'observaciones',
    ];
    const visibles = this.mostrarProveedor() ? base : base.filter((c) => c !== 'proveedor');
    // Correcting and voiding are for the Admin only (the API refuses everybody else anyway): nobody else gets the menu column
    return this.auth.esAdmin() ? [...visibles, 'acciones'] : visibles;
  });

  protected filas = computed<Fila[]>(() => {
    const subtotales = new Map(this.dias().map((d) => [d.fecha, d]));
    const filas: Fila[] = [];
    let diaActual = '';
    for (const recepcion of this.items()) {
      const dia = recepcion.fechaHora.slice(0, 10);
      if (dia !== diaActual) {
        diaActual = dia;
        filas.push({ tipo: 'dia', etiqueta: etiquetaDia(dia), subtotal: subtotales.get(dia) ?? null });
      }
      filas.push({ tipo: 'recepcion', recepcion });
    }
    return filas;
  });

  protected esDia = (_: number, fila: Fila): boolean => fila.tipo === 'dia';
  protected esRecepcion = (_: number, fila: Fila): boolean => fila.tipo === 'recepcion';

  protected dia(fila: Fila): FilaDia {
    return fila as FilaDia;
  }

  protected r(fila: Fila): RecepcionListado {
    return (fila as FilaRecepcion).recepcion;
  }

  protected hora(r: RecepcionListado): string {
    return r.fechaHora.slice(11, 16);
  }

  protected fuera(r: RecepcionListado, motivo: MotivoFueraDeRango): boolean {
    return r.motivos.includes(motivo);
  }

  protected etiquetaEstado(r: RecepcionListado): string {
    const [primero, ...resto] = r.motivos;
    return resto.length > 0 ? `${ETIQUETA_MOTIVO[primero]} +${resto.length}` : ETIQUETA_MOTIVO[primero];
  }

  protected detalleEstado(r: RecepcionListado): string {
    return r.motivos.map((m) => ETIQUETA_MOTIVO[m]).join(', ');
  }

  protected detalleAnulada(r: RecepcionListado): string {
    const cuando = r.anuladaEn ? ` el ${textoInstante(r.anuladaEn)}` : '';
    return `Anulada por ${r.anuladaPor ?? '—'}${cuando}. Motivo: ${r.motivoAnulacion ?? '—'}`;
  }

  protected textoCorregida(r: RecepcionListado): string {
    return `Corregida ${r.cantidadCorrecciones} ${r.cantidadCorrecciones === 1 ? 'vez' : 'veces'}. Ver historial`;
  }

  /** The history is read by every role that reads the list. */
  protected verHistorial(r: RecepcionListado): void {
    this.dialog.open<HistorialDialog, HistorialDialogData>(HistorialDialog, {
      data: {
        titulo: 'Historial de la recepción',
        contexto: `Ticket ${r.ticket ?? '—'} · ${r.proveedorNombre} · ${textoFechaHora(r.fechaHora)}`,
        cargar: () => this.recepcionesService.getHistorial(r.id),
      },
      width: '520px',
      maxWidth: '95vw',
    });
  }

  protected anular(r: RecepcionListado): void {
    this.dialog
      .open<MotivoDialog, MotivoDialogData, boolean>(MotivoDialog, {
        data: {
          titulo: 'Anular recepción',
          mensaje: `La recepción del ticket ${r.ticket ?? '—'} (${r.proveedorNombre}) queda anulada para siempre: no suma en ningún total y su ticket no se puede volver a usar. Sigue visible con "Incluir anuladas".`,
          confirmar: 'Anular',
          peligro: true,
          guardar: (motivo) => this.recepcionesService.anular(r.id, motivo),
        },
        role: 'alertdialog',
        width: '480px',
        maxWidth: '95vw',
      })
      .afterClosed()
      .subscribe((anulada) => {
        if (anulada) {
          this.snackBar.open('Recepción anulada.', 'Cerrar', { duration: 4000 });
          this.cambio.emit();
        }
      });
  }

  protected verObservaciones(r: RecepcionListado): void {
    this.dialog.open<ObservacionesDialog, ObservacionesDialogData>(ObservacionesDialog, {
      data: {
        ticket: r.ticket,
        proveedor: r.proveedorNombre,
        fechaHora: r.fechaHora,
        observaciones: r.observaciones ?? '',
      },
      width: '480px',
      maxWidth: '95vw',
    });
  }
}

/** "Jueves 15/10/2026" for a YYYY-MM-DD date (day of the week in es-AR, whatever the browser's zone is). */
export function etiquetaDia(iso: string): string {
  const [anio, mes, dia] = iso.split('-').map(Number);
  const nombre = new Intl.DateTimeFormat('es-AR', { weekday: 'long', timeZone: 'UTC' }).format(
    new Date(Date.UTC(anio, mes - 1, dia)),
  );
  return `${nombre.charAt(0).toUpperCase()}${nombre.slice(1)} ${formatearFecha(iso)}`;
}
