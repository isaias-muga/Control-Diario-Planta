import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth';
import { ProveedoresService } from '../../services/proveedores';
import { Proveedor } from '../../models/proveedor';
import { CrearProveedorDialog } from '../../components/crear-proveedor-dialog/crear-proveedor-dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatDialog } from '@angular/material/dialog';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

@Component({
  selector: 'app-proveedores-list',
  imports: [
    RouterLink,
    MatCardModule,
    MatButtonModule,
    MatDividerModule,
    MatListModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './proveedores-list.html',
  styleUrl: './proveedores-list.css',
})
export class ProveedoresList {
  protected auth = inject(AuthService);
  private proveedoresService = inject(ProveedoresService);
  private snackBar = inject(MatSnackBar);
  private dialog = inject(MatDialog);

  proveedores = signal<Proveedor[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);

  constructor() {
    this.proveedoresService.getAll().subscribe({
      next: (data) => {
        this.proveedores.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('No se pudieron cargar los proveedores.');
        this.loading.set(false);
      },
    });
  }

  protected abrirCrear(): void {
    this.dialog
      .open<CrearProveedorDialog, void, Proveedor>(CrearProveedorDialog, {
        width: '480px',
        maxWidth: '95vw',
      })
      .afterClosed()
      .subscribe((creado) => {
        if (creado) {
          this.proveedores.update((lista) => [...lista, creado]);
          this.snackBar.open('Proveedor creado.', 'Cerrar', { duration: 4000 });
        }
      });
  }
}
