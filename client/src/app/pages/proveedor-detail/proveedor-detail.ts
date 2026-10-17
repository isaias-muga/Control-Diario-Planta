import { Component, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { AuthService } from '../../services/auth';
import { ProveedoresService } from '../../services/proveedores';
import { Proveedor } from '../../models/proveedor';
import { RecepcionesPanel } from '../../components/recepciones-panel/recepciones-panel';
import { RegistrarRecepcionDialog } from '../../components/registrar-recepcion-dialog/registrar-recepcion-dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

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

  protected abrirRegistro(proveedorId: string): void {
    RegistrarRecepcionDialog.abrir(this.dialog, { proveedorId }).subscribe((registro) => {
      if (registro) {
        this.panel()?.recargar();
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
