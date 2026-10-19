import { Component, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { AuthService } from '../../services/auth';
import { ProveedoresService } from '../../services/proveedores';
import { Proveedor } from '../../models/proveedor';
import { RecepcionesPanel } from '../../components/recepciones-panel/recepciones-panel';
import { HistorialDialog, HistorialDialogData } from '../../components/historial-dialog/historial-dialog';
import { MotivoDialog, MotivoDialogData } from '../../components/motivo-dialog/motivo-dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';

@Component({
  selector: 'app-proveedor-detail',
  imports: [RouterLink, MatButtonModule, MatProgressSpinnerModule, RecepcionesPanel],
  templateUrl: './proveedor-detail.html',
  styleUrl: './proveedor-detail.css',
})
export class ProveedorDetail {
  protected auth = inject(AuthService);
  private proveedoresService = inject(ProveedoresService);
  private dialog = inject(MatDialog);
  private snackBar = inject(MatSnackBar);
  private request?: Subscription;
  private panel = viewChild(RecepcionesPanel);

  id = input.required<string>();

  proveedor = signal<Proveedor | null>(null);
  loading = signal(true);
  error = signal<string | null>(null);
  notFound = signal(false);

  constructor() {
    effect(() => {
      this.id(); // re-run whenever the route id changes
      untracked(() => this.load());
    });
  }

  /** Corrects the name, with a reason: the old name stays in the history. */
  protected corregirNombre(p: Proveedor): void {
    this.abrirMotivo(
      {
        titulo: 'Corregir nombre',
        mensaje: 'El nombre anterior queda en el historial del proveedor.',
        confirmar: 'Corregir',
        nombre: { etiqueta: 'Nombre', valor: p.nombre },
        guardar: (motivo, nombre) => this.proveedoresService.renombrar(p.id, nombre ?? p.nombre, motivo, p.version ?? ''),
      },
      'Nombre corregido.',
    );
  }

  /** A proveedor is never deleted: it is deactivated (not offered for new recepciones, all history kept) or reactivated. */
  protected cambiarEstado(p: Proveedor): void {
    if (p.activo) {
      this.abrirMotivo(
        {
          titulo: 'Desactivar proveedor',
          mensaje: `${p.nombre} deja de ofrecerse al registrar recepciones. Sus recepciones y su historial se conservan, y se puede reactivar.`,
          confirmar: 'Desactivar',
          peligro: true,
          guardar: (motivo) => this.proveedoresService.desactivar(p.id, motivo),
        },
        'Proveedor desactivado.',
      );
    } else {
      this.abrirMotivo(
        {
          titulo: 'Reactivar proveedor',
          mensaje: `${p.nombre} vuelve a ofrecerse al registrar recepciones.`,
          confirmar: 'Reactivar',
          guardar: (motivo) => this.proveedoresService.reactivar(p.id, motivo),
        },
        'Proveedor reactivado.',
      );
    }
  }

  protected verHistorial(p: Proveedor): void {
    this.dialog.open<HistorialDialog, HistorialDialogData>(HistorialDialog, {
      data: {
        titulo: 'Historial del proveedor',
        contexto: p.nombre,
        cargar: () => this.proveedoresService.getHistorial(p.id),
      },
      width: '520px',
      maxWidth: '95vw',
    });
  }

  private abrirMotivo(data: MotivoDialogData, mensaje: string): void {
    this.dialog
      .open<MotivoDialog, MotivoDialogData, boolean>(MotivoDialog, { data, width: '480px', maxWidth: '95vw' })
      .afterClosed()
      .subscribe((guardado) => {
        if (guardado) {
          this.snackBar.open(mensaje, 'Cerrar', { duration: 4000 });
          this.load(false);
          this.panel()?.recargar(); // the rows show the proveedor's name
        }
      });
  }

  protected load(showSpinner = true): void {
    this.request?.unsubscribe();

    if (showSpinner) {
      this.loading.set(true);
    }
    this.error.set(null);
    this.notFound.set(false);

    // The endpoint returns only the name and the count: the recepciones come from the paged list endpoint (the panel).
    this.request = this.proveedoresService.getById(this.id()).subscribe({
      next: (proveedor) => {
        this.proveedor.set(proveedor);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.proveedor.set(null);
        if (err.status === 404 || err.status === 400) { // 400: the id in the URL is not a valid Guid
          this.notFound.set(true);
        } else {
          this.error.set('No se pudo cargar el proveedor.');
        }
        this.loading.set(false);
      },
    });
  }
}
