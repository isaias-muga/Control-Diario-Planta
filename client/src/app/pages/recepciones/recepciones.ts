import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { RouterLink } from '@angular/router';
import { Proveedor } from '../../models/proveedor';
import { FiltroRecepciones } from '../../models/recepcion-listado';
import { ProveedoresService } from '../../services/proveedores';
import { RecepcionesPanel } from '../../components/recepciones-panel/recepciones-panel';
import { RegistrarRecepcionDialog } from '../../components/registrar-recepcion-dialog/registrar-recepcion-dialog';
import { formatearFecha } from '../../utils/periodos';

@Component({
  selector: 'app-recepciones',
  imports: [MatButtonModule, RouterLink, RecepcionesPanel],
  templateUrl: './recepciones.html',
  styleUrl: './recepciones.css',
})
export class Recepciones {
  private dialog = inject(MatDialog);
  private proveedoresService = inject(ProveedoresService);

  private panel = viewChild.required(RecepcionesPanel);
  private filtros = signal<FiltroRecepciones | null>(null);
  private proveedores = signal<Proveedor[]>([]);

  protected subtitulo = computed(() => {
    const f = this.filtros();
    if (!f) {
      return '';
    }
    const periodo =
      f.desde === f.hasta
        ? formatearFecha(f.desde)
        : `${formatearFecha(f.desde)} – ${formatearFecha(f.hasta)}`;
    const nombre = this.proveedores().find((p) => p.id === f.proveedorId)?.nombre;
    return `${periodo} · ${nombre ?? 'todos los proveedores'}`;
  });

  constructor() {
    this.proveedoresService.getAll().subscribe({
      next: (lista) => this.proveedores.set(lista),
      error: () => this.proveedores.set([]), // the subtitle just omits the name
    });
  }

  protected alCambiarFiltros(filtros: FiltroRecepciones): void {
    this.filtros.set(filtros);
  }

  protected abrirRegistro(): void {
    RegistrarRecepcionDialog.abrir(this.dialog, {}).subscribe((registro) => {
      if (registro) {
        this.panel().recargar();
      }
    });
  }
}
