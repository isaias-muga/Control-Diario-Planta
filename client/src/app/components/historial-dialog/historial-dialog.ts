import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { Observable } from 'rxjs';
import { Correccion } from '../../models/correcciones';
import { getApiErrorMessage } from '../../utils/api-error';
import { etiquetaCampo, textoInstante, textoValor } from '../../utils/historial';

export interface HistorialDialogData {
  titulo: string;
  /** What the history is about, one line (for example "Ticket 4980 · Tambo Don Pedro"). */
  contexto?: string;
  cargar: () => Observable<Correccion[]>;
}

/** The history of a recepción or a proveedor: every correction, void or deactivation with who, when, why and old to new. */
@Component({
  selector: 'app-historial-dialog',
  imports: [MatDialogModule, MatButtonModule],
  templateUrl: './historial-dialog.html',
  styleUrl: './historial-dialog.css',
})
export class HistorialDialog {
  protected data = inject<HistorialDialogData>(MAT_DIALOG_DATA);

  protected cargando = signal(true);
  protected error = signal<string | null>(null);
  protected items = signal<Correccion[]>([]);

  protected readonly etiqueta = etiquetaCampo;
  protected readonly valor = textoValor;
  protected readonly instante = textoInstante;

  constructor() {
    this.data.cargar().subscribe({
      next: (items) => {
        this.items.set(items);
        this.cargando.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.error.set(getApiErrorMessage(err, 'No se pudo cargar el historial.'));
        this.cargando.set(false);
      },
    });
  }
}
